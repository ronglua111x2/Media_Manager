using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeminiSpecialEval.Cases;
using GeminiSpecialEval.Infrastructure;
using GeminiSpecialEval.Prompts;
using GeminiSpecialEval.Recording;
using GeminiSpecialEval.Running;
using GeminiSpecialEval.Scoring;
using media_management_app.Common;
using media_management_app.Services;
using media_management_app.Services.Gemini.Routing;

namespace GeminiSpecialEval;

internal sealed record PlannedSample(CaseProbeResult Probe, string Model, PromptVariant Variant, int Sample)
{
    public string Key => $"{Probe.Case.Name}|{Model}|{Variant.Name}|{Sample}";
}

internal static class Commands
{
    /// <summary>SHA-256 of the frozen v2 prompt (LF newlines), checked against commit 92e6b58 when it was frozen.</summary>
    private const string V2PromptSha256Lf = "dcc995eb8335142a4faf6dac761c0fa7b6648f52ea07c0e02c9d6a17cad80618";

    // ---------------------------------------------------------------- list / plan / quota

    public static int List(Options options)
    {
        var notes = new List<string>();
        var dataDir = options.DataDir(required: false);
        var cases = LoadCases(options, dataDir, notes);
        var variants = Variants(options, defaultAll: true);
        Console.WriteLine($"Live prompt: {PromptVariant.LiveName} (sha {Json.Short(CaseProbe.PromptSha(PromptVariant.Get(PromptVariant.LiveName)))})");
        foreach (var variant in variants.Where(item => !item.IsLive))
        {
            Console.WriteLine($"Frozen prompt: {variant.Name} (sha {Json.Short(CaseProbe.PromptSha(variant))})");
        }

        Console.WriteLine();
        Console.WriteLine($"{"case",-30} {"source",-8} {"specials",8} {"rules ok/✗",11} {"to Gemini",9} {"fx",3} {"chars " + string.Join("/", variants.Select(item => item.Name)),18}  tags");
        foreach (var evalCase in cases)
        {
            var probe = CaseProbe.Probe(evalCase);
            var rules = Scorer.ScoreRules(probe);
            var chars = probe.CallsGemini ? string.Join("/", variants.Select(variant => probe.PayloadFor(variant).Length)) : "–";
            Console.WriteLine(
                $"{evalCase.Name,-30} {evalCase.Source,-8} {evalCase.ExpectedS00E.Count,8} " +
                $"{$"{rules.Count(rule => rule.Outcome == RuleOutcome.Correct)}/{rules.Count(rule => rule.Outcome == RuleOutcome.Wrong)}",11} " +
                $"{probe.Request.GeminiCandidateIndices.Count,9} {probe.Request.Proposals.Count,3} {chars,18}  {string.Join(",", evalCase.Tags)}");
            if (options.Has("verbose") && probe.CallsGemini)
            {
                Console.WriteLine("    " + probe.LivePayload);
            }
        }

        PrintNotes(notes);
        return 0;
    }

    public static int Plan(Options options)
    {
        var dataDir = options.DataDir(required: true)!;
        var notes = new List<string>();
        var plan = BuildPlan(options, dataDir, notes, out _);
        var ledger = new QuotaLedger(dataDir, options.GetInt("max-requests"));
        PrintPlan(plan, ledger);
        PrintNotes(notes);
        return CheckBudget(plan, ledger) is { } problem ? Fail(problem) : 0;
    }

    public static int Quota(Options options)
    {
        var dataDir = options.DataDir(required: true)!;
        var ledger = new QuotaLedger(dataDir);
        Console.WriteLine($"Pacific day {ledger.PacificDay}. Caps: {QuotaLedger.LargeSharedCap}/day over all 500/day models, {QuotaLedger.SmallPerModelCap}/day per 20/day model.");
        var used = ledger.UsedTodayByModel();
        foreach (var entry in GeminiModelCatalogDefaults.CreateSeed().Models.Where(model => QuotaLedger.Classify(model.Id) != QuotaClass.Unknown))
        {
            Console.WriteLine($"  {entry.Id,-26} used {used.GetValueOrDefault(entry.Id),3}  left {ledger.Remaining(entry.Id),3}  ({QuotaLedger.Classify(entry.Id)})");
        }

        return 0;
    }

    // ---------------------------------------------------------------- run

    public static async Task<int> RunAsync(Options options, CancellationToken cancellationToken)
    {
        var dataDir = options.DataDir(required: true)!;
        var notes = new List<string>();
        var runId = options.Get("resume") ?? $"{DateTime.UtcNow:yyyyMMdd-HHmmss}{(options.Get("label") is { } label ? "-" + RecordSafe(label) : string.Empty)}";
        var runFolder = Path.Combine(dataDir, "runs", runId);
        var samplesFolder = Path.Combine(runFolder, "samples");
        var plan = BuildPlan(options, dataDir, notes, out var allProbes);

        if (options.Get("resume") is not null && Directory.Exists(samplesFolder))
        {
            var done = Directory.EnumerateFiles(samplesFolder, "*.json")
                .Select(SampleRecord.Load)
                .Where(record => record.Status != SampleStatus.Retried)
                .Select(record => $"{record.Case}|{record.Model}|{record.Variant}|{record.Sample}")
                .ToHashSet(StringComparer.Ordinal);
            plan = plan.Where(item => !done.Contains(item.Key)).ToList();
            Console.WriteLine($"Resuming {runId}: {done.Count} sample(s) already recorded.");
        }

        var ledger = new QuotaLedger(dataDir, options.GetInt("max-requests"));
        PrintPlan(plan, ledger);
        PrintNotes(notes);
        if (CheckBudget(plan, ledger) is { } problem)
        {
            return Fail(problem);
        }

        if (!options.Has("yes"))
        {
            Console.WriteLine("Nothing sent. Add --yes to send these requests.");
            return 0;
        }

        // --offline-responder: a dry rehearsal of the whole run (ledger, records, retries, report) against fake
        // answers instead of the network. Use a scratch data folder: the rehearsal counts in that folder's ledger.
        var rehearsal = options.Get("offline-responder");
        string key;
        if (rehearsal is not null)
        {
            key = "offline-rehearsal-key";
            Console.WriteLine($"REHEARSAL: answers come from the '{rehearsal}' responder; nothing is sent.");
        }
        else
        {
            var (resolved, origin) = KeySource.Resolve(options.Get("key-from-settings"));
            if (resolved is null)
            {
                return Fail(origin);
            }

            key = resolved;
            SafeFile.RegisterSecret(key);
            Console.WriteLine($"API key: from {origin}.");
        }

        var variants = plan.Select(item => item.Variant).DistinctBy(item => item.Name).ToList();
        WriteManifest(runFolder, runId, options, plan, variants, allProbes);

        var stacks = new Dictionary<string, (GeminiStack Stack, LiveGuardHandler Guard)>(StringComparer.OrdinalIgnoreCase);
        var unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var model in plan.Select(item => item.Model).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var guard = new LiveGuardHandler(ledger, model, rehearsal is null ? null : RehearsalNetwork(rehearsal, allProbes));
                stacks[model] = (GeminiStack.Live(Path.Combine(dataDir, "app-state", RecordSafe(model)), model, key, guard), guard);
                stacks[model].Stack.Logger.Echo = options.Has("verbose");
            }

            var position = 0;
            foreach (var planned in plan)
            {
                position++;
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("Stopped by the user.");
                    break;
                }

                if (unavailable.Contains(planned.Model))
                {
                    continue;
                }

                var (stack, guard) = stacks[planned.Model];
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    if (ledger.Remaining(planned.Model) < 1)
                    {
                        Console.WriteLine($"[{position}/{plan.Count}] {planned.Model}: eval cap reached for today; skipping the rest for this model.");
                        unavailable.Add(planned.Model);
                        break;
                    }

                    if (!await WaitForModelAsync(stack, cancellationToken))
                    {
                        Console.WriteLine($"[{position}/{plan.Count}] {planned.Model}: locked by the router ({DescribeRouter(stack)}); skipping the rest for this model.");
                        unavailable.Add(planned.Model);
                        break;
                    }

                    var exchanges = new List<RecordedExchange>();
                    guard.Sink = exchanges;
                    guard.Label = $"{planned.Probe.Case.Name}/{planned.Variant.Name}/s{planned.Sample}";
                    var started = DateTimeOffset.UtcNow;
                    var run = await stack.RunCaseAsync(planned.Probe.Case, planned.Variant, CancellationToken.None);
                    guard.Sink = null;
                    var record = BuildRecord(runId, planned, run, exchanges, started, attempt);
                    var retry = record.Status == SampleStatus.Error && attempt == 1 && !options.Has("no-retry") && IsTransient(exchanges);
                    if (retry)
                    {
                        record.Status = SampleStatus.Retried;
                    }

                    record.Save(samplesFolder);
                    var score = Scorer.ScoreSample(record, planned.Probe, run, stale: false, replayNote: null);
                    Console.WriteLine($"[{position}/{plan.Count}] {planned.Probe.Case.Name} · {planned.Model} · {planned.Variant.Name} · s{planned.Sample}: {Describe(record, score)}");
                    if (!retry)
                    {
                        break;
                    }

                    Console.WriteLine("    transient failure; retrying once when the router frees the model…");
                }
            }
        }
        finally
        {
            foreach (var (stack, _) in stacks.Values)
            {
                stack.Dispose();
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Requests sent by this run: {ledger.RunSpent}. Re-scoring the recordings (zero requests)…");
        var reportPath = await WriteReplayReportAsync(dataDir, [runId], Path.Combine(runFolder, "report.md"), $"Gemini special eval, run {runId}", options.Has("include-stale"), cancellationToken);
        Console.WriteLine($"Report: {reportPath}");
        return 0;
    }

    private static SampleRecord BuildRecord(
        string runId,
        PlannedSample planned,
        CaseRun run,
        List<RecordedExchange> exchanges,
        DateTimeOffset started,
        int attempt)
    {
        var parsed = run.Client.ResponseText is { } text && SpecialMappingCompactJson.TryParseResponse(text, out _, out _);
        var request = planned.Probe.Request;
        return new SampleRecord
        {
            RunId = runId,
            Case = planned.Probe.Case.Name,
            CaseSource = planned.Probe.Case.Source,
            Model = planned.Model,
            Variant = planned.Variant.Name,
            PromptVersion = planned.Variant.Version,
            PromptSha256 = Json.Sha256(run.Client.SentPrompt ?? planned.Variant.RewritePrompt(CaseProbe.LivePrompt())),
            PayloadSha256 = Json.Sha256(run.Client.SentPayload ?? planned.Probe.PayloadFor(planned.Variant)),
            Sample = planned.Sample,
            Attempt = attempt,
            StartedUtc = started,
            PacificDay = GeminiModelAvailabilityStore.PacificDayKey(started),
            Status = parsed ? SampleStatus.Ok : SampleStatus.Error,
            Payload = run.Client.SentPayload,
            Exchanges = exchanges.ToList(),
            ResponseText = run.Client.ResponseText,
            AnsweredBy = run.Client.AnsweredBy,
            FinishReason = run.Client.FinishReason,
            Error = parsed ? null : SafeFile.Scrub(run.Client.Error ?? run.Result.FallbackReason ?? "no answer"),
            FinalAtRecord = request.GeminiCandidateIndices
                .Select(index => request.Context.Candidates[index].RelativePath)
                .ToDictionary(path => path, path => run.Result.ByPath.GetValueOrDefault(path)?.Episode?.EpisodeNumber)
        };
    }

    /// <summary>503, timeout or a per-minute 429: worth one more try once the router frees the model.</summary>
    private static bool IsTransient(IReadOnlyList<RecordedExchange> exchanges) =>
        exchanges.Count > 0 &&
        exchanges[^1] is var last &&
        (last.ErrorKind == "timeout" || last.Status is 503 or 500 or 502 or 504 ||
         (last.Status == 429 && last.Body?.Contains("PerMinute", StringComparison.Ordinal) == true));

    /// <summary>Waits (up to 3 minutes) while the router has the model locked for a short time.</summary>
    private static async Task<bool> WaitForModelAsync(GeminiStack stack, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
        while (true)
        {
            var decision = stack.Router.Select(new HashSet<string>());
            if (decision.Selected is not null)
            {
                return true;
            }

            if (decision.NextAvailableUtc is not { } next || next > deadline)
            {
                return false;
            }

            var wait = next - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1);
            if (wait > TimeSpan.Zero)
            {
                Console.WriteLine($"    {stack.Model} locked by the router; waiting {Math.Ceiling(wait.TotalSeconds)} s…");
                await Task.Delay(wait, cancellationToken);
            }
        }
    }

    private static string DescribeRouter(GeminiStack stack) =>
        stack.Router.Select(new HashSet<string>()).UnavailableReason ?? "unavailable";

    private static string Describe(SampleRecord record, SampleScore score)
    {
        var latency = record.Exchanges.LastOrDefault()?.ElapsedMs is { } ms ? $"{ms / 1000.0:0.0} s" : "–";
        var http = string.Join(",", record.Exchanges.Select(exchange => exchange.Status?.ToString(CultureInfo.InvariantCulture) ?? exchange.ErrorKind));
        if (record.Status != SampleStatus.Ok)
        {
            return $"{record.Status.ToUpperInvariant()} (HTTP {http}, {latency}): {Truncate(record.Error ?? string.Empty, 140)}";
        }

        int Count(GeminiOutcome outcome) => score.Files.Count(file => file.Outcome == outcome);
        return $"✓{Count(GeminiOutcome.Correct)} ✗{Count(GeminiOutcome.Wrong)} ⊘{Count(GeminiOutcome.DroppedRight) + Count(GeminiOutcome.DroppedWrong)} " +
               $"–{Count(GeminiOutcome.LeftOut)} abst✓{Count(GeminiOutcome.AbstainCorrect)} of {score.Files.Count} (HTTP {http}, {latency})";
    }

    private static void WriteManifest(
        string runFolder,
        string runId,
        Options options,
        IReadOnlyList<PlannedSample> plan,
        IReadOnlyList<PromptVariant> variants,
        IReadOnlyList<CaseProbeResult> selected)
    {
        var manifest = new JsonObject
        {
            ["runId"] = runId,
            ["startedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["pacificDay"] = GeminiModelAvailabilityStore.PacificDayKey(DateTimeOffset.UtcNow),
            ["harness"] = GitInfo.Describe(),
            ["livePrompt"] = PromptVariant.LiveName,
            ["prompts"] = new JsonObject(variants.Select(variant =>
                KeyValuePair.Create(variant.Name, (JsonNode?)JsonValue.Create(CaseProbe.PromptSha(variant))))),
            ["models"] = new JsonArray(plan.Select(item => item.Model).Distinct().Select(model => (JsonNode?)JsonValue.Create(model)).ToArray()),
            ["cases"] = new JsonArray(plan.Select(item => item.Probe.Case.Name).Distinct().Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            ["casesSelected"] = new JsonArray(selected.Select(probe => (JsonNode?)JsonValue.Create(probe.Case.Name)).ToArray()),
            ["plannedRequests"] = plan.Count,
            ["samples"] = options.GetInt("samples") ?? 1
        };
        SafeFile.WriteAllText(Path.Combine(runFolder, "run.json"), manifest.ToJsonString(Json.WriteOptions));
    }

    // ---------------------------------------------------------------- replay

    public static async Task<int> ReplayAsync(Options options, CancellationToken cancellationToken)
    {
        var dataDir = options.DataDir(required: true)!;
        var runs = options.GetList("run");
        if (runs.Count == 0)
        {
            return Fail("Pass --run <runId>[,<runId>…] (folders under runs/ in the data folder).");
        }

        var name = runs.Count == 1 ? runs[0] : $"{runs.Count}-runs";
        var output = Path.Combine(dataDir, "reports", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-replay-{RecordSafe(name)}.md");
        var path = await WriteReplayReportAsync(dataDir, runs, output, $"Gemini special eval, replay of {string.Join(", ", runs)}", options.Has("include-stale"), cancellationToken);
        Console.WriteLine($"Report: {path}");
        return 0;
    }

    /// <summary>Re-runs every recorded sample of <paramref name="runIds"/> through today's app code with zero requests.</summary>
    private static async Task<string> WriteReplayReportAsync(
        string dataDir,
        IReadOnlyList<string> runIds,
        string outputPath,
        string title,
        bool includeStale,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var cases = CaseCatalog.Synthetic().Concat(CaseCatalog.Private(dataDir, includeUnconfirmed: true, notes))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var records = new List<SampleRecord>();
        foreach (var runId in runIds)
        {
            var folder = Path.Combine(dataDir, "runs", runId, "samples");
            if (!Directory.Exists(folder))
            {
                notes.Add($"Run {runId}: no samples folder.");
                continue;
            }

            records.AddRange(Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal).Select(SampleRecord.Load));
        }

        var retried = records.Where(record => record.Status == SampleStatus.Retried).ToList();
        var scores = new List<SampleScore>();
        var probes = new Dictionary<string, CaseProbeResult>(StringComparer.Ordinal);
        foreach (var record in records.Except(retried))
        {
            if (!cases.TryGetValue(record.Case, out var evalCase))
            {
                notes.Add($"{record.Case}: case no longer exists; {record.FileName} not scored.");
                continue;
            }

            PromptVariant variant;
            try
            {
                variant = PromptVariant.Get(record.Variant);
            }
            catch (ArgumentException ex)
            {
                notes.Add($"{record.FileName}: {ex.Message}");
                continue;
            }

            if (!probes.TryGetValue(record.Case, out var probe))
            {
                probes[record.Case] = probe = CaseProbe.Probe(evalCase);
            }

            var reasons = new List<string>();
            if (Json.Sha256(probe.PayloadFor(variant)) != record.PayloadSha256)
            {
                reasons.Add("the app now builds a different payload for this case");
            }

            if (CaseProbe.PromptSha(variant) != record.PromptSha256)
            {
                reasons.Add($"the {variant.Name} prompt text changed since the recording");
            }

            var stale = reasons.Count > 0 && !includeStale;
            using var stack = GeminiStack.Offline(record.Model, new ReplayHandler(record.Exchanges));
            var run = await stack.RunCaseAsync(evalCase, variant, cancellationToken);
            var finals = record.FinalAtRecord.Keys.ToDictionary(path => path, path => run.Result.ByPath.GetValueOrDefault(path)?.Episode?.EpisodeNumber);
            if (reasons.Count == 0 && record.FinalAtRecord.Count > 0 &&
                record.FinalAtRecord.Any(pair => finals.GetValueOrDefault(pair.Key) != pair.Value))
            {
                reasons.Add("today's checks give a different final mapping than at record time (app code changed)");
            }

            scores.Add(Scorer.ScoreSample(record, probe, run, stale, reasons.Count == 0 ? null : string.Join("; ", reasons)));
        }

        // Cases the runs selected that never needed Gemini still belong in the rules table.
        foreach (var runId in runIds)
        {
            var manifestPath = Path.Combine(dataDir, "runs", runId, "run.json");
            if (File.Exists(manifestPath) && JsonNode.Parse(File.ReadAllText(manifestPath))?["casesSelected"] is JsonArray selected)
            {
                foreach (var name in selected.Select(node => node?.GetValue<string>()).OfType<string>())
                {
                    if (!probes.ContainsKey(name) && cases.TryGetValue(name, out var evalCase))
                    {
                        probes[name] = CaseProbe.Probe(evalCase);
                    }
                }
            }
        }

        var caseRows = probes.Values
            .OrderBy(probe => probe.Case.Source, StringComparer.Ordinal)
            .ThenBy(probe => probe.Case.Name, StringComparer.Ordinal)
            .Select(probe => (probe, Scorer.ScoreRules(probe)))
            .ToList();
        var requests = records.Sum(record => record.Exchanges.Count(exchange => exchange.ErrorKind != "budget"));
        var header = new List<string>
        {
            $"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC by `GeminiSpecialEval` ({GitInfo.Describe()}); scored by replaying the recordings through today's app code (0 requests).",
            $"Runs: {string.Join(", ", runIds)}; Pacific day(s): {string.Join(", ", records.Select(record => record.PacificDay).Distinct().Order())}.",
            $"Prompts: {string.Join("; ", records.Select(record => record.Variant).Distinct().Order().Select(variantName => $"{variantName} sha {Json.Short(records.First(record => record.Variant == variantName).PromptSha256)}"))}. Live app prompt: {PromptVariant.LiveName}.",
            $"Samples: {records.Count - retried.Count} scored + {retried.Count} retried attempt(s); requests recorded: {requests}.",
            $"Cases: {probes.Count} ({string.Join(", ", probes.Values.GroupBy(probe => probe.Case.Source).Select(group => $"{group.Count()} {group.Key}"))})."
        };
        var report = ReportWriter.Build(title, header, scores, caseRows, notes);
        SafeFile.WriteAllText(outputPath, report);
        PrintSummary(scores);
        return outputPath;
    }

    // ---------------------------------------------------------------- selftest

    public static async Task<int> SelfTestAsync(Options options, CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        var live = PromptVariant.Get(PromptVariant.LiveName);
        var v2 = PromptVariant.Get("v2");

        // 1. The frozen v2 prompt is the one of commit 92e6b58.
        var v2Sha = Json.Sha256(v2.RewritePrompt("lf only\n"));
        Console.WriteLine($"v2 prompt sha (LF): {v2Sha}");
        if (v2Sha != V2PromptSha256Lf)
        {
            failures.Add("the frozen v2 prompt no longer matches commit 92e6b58");
        }

        // 2. Payload v2 = payload v3 with v=2 and two-item fx entries, nothing else.
        var cases = CaseCatalog.Synthetic();
        var probes = cases.Select(CaseProbe.Probe).Where(probe => probe.CallsGemini).ToList();
        foreach (var probe in probes)
        {
            var three = JsonNode.Parse(probe.LivePayload!)!.AsObject();
            var two = JsonNode.Parse(probe.PayloadFor(v2))!.AsObject();
            if (two["v"]!.GetValue<int>() != 2 || two["ctx"]!["fx"]!.AsArray().Any(pair => pair!.AsArray().Count != 2))
            {
                failures.Add($"{probe.Case.Name}: v2 payload has the wrong version or fx shape");
            }

            three["v"] = 2;
            foreach (var pair in three["ctx"]!["fx"]!.AsArray().OfType<JsonArray>())
            {
                while (pair.Count > 2)
                {
                    pair.RemoveAt(pair.Count - 1);
                }
            }

            if (!JsonNode.DeepEquals(three, two))
            {
                failures.Add($"{probe.Case.Name}: v2 payload differs from v3 beyond v and fx");
            }
        }

        Console.WriteLine($"{probes.Count} synthetic case(s) call Gemini; {cases.Count - probes.Count} are placed by the rules alone.");

        // 3. Fake answers through the real client, provider, orchestrator and checks.
        var responders = new (string Name, Func<EvalCase, string, string> Answer)[]
        {
            ("oracle", (evalCase, payload) => Answer(evalCase, payload, expected => expected)),
            ("shifted", (evalCase, payload) => Answer(evalCase, payload, expected => expected + 1)),
            ("silent", (_, _) => "{\"m\":[],\"w\":[]}")
        };
        var scores = new List<SampleScore>();
        foreach (var (name, answer) in responders)
        {
            foreach (var probe in probes)
            {
                var handler = new ResponderHandler(payload => answer(probe.Case, payload));
                using var stack = GeminiStack.Offline("gemini-3.5-flash-lite", handler);
                var run = await stack.RunCaseAsync(probe.Case, live, cancellationToken);
                var record = new SampleRecord
                {
                    RunId = "selftest",
                    Case = probe.Case.Name,
                    Model = $"selftest-{name}",
                    Variant = live.Name,
                    Sample = 1,
                    Exchanges = handler.Exchanges,
                    Status = SampleStatus.Ok
                };
                scores.Add(Scorer.ScoreSample(record, probe, run, stale: false, replayNote: null));
            }
        }

        var cells = ReportWriter.BuildCells(scores).ToDictionary(cell => cell.Model);
        var oracle = cells["selftest-oracle"];
        var shifted = cells["selftest-shifted"];
        var silent = cells["selftest-silent"];
        Expect(oracle.Count(GeminiOutcome.Wrong) == 0, "oracle answers produced a wrong link");
        Expect(oracle.Count(GeminiOutcome.LeftOut) == 0, "oracle answers left a file out");
        Expect(oracle.Count(GeminiOutcome.Correct) + oracle.Count(GeminiOutcome.DroppedRight) == oracle.RealSpecials, "oracle answers were not all counted correct (or dropped right)");
        Expect(shifted.Count(GeminiOutcome.Correct) == 0, "shifted answers were scored correct");
        Expect(shifted.Count(GeminiOutcome.Wrong) + shifted.Count(GeminiOutcome.DroppedWrong) > 0, "shifted answers were not scored wrong or dropped");
        Expect(silent.Count(GeminiOutcome.Wrong) == 0 && silent.Count(GeminiOutcome.Correct) == 0, "silent answers linked something");
        Expect(silent.Count(GeminiOutcome.LeftOut) == silent.RealSpecials, "silent answers were not all left out");
        Expect(ReportWriter.WilsonUpper(0, 20) is > 0.16 and < 0.17, "Wilson upper bound for 0/20 is not about 16.1%");

        foreach (var drop in scores.Where(score => score.Record.Model == "selftest-oracle")
                     .SelectMany(score => score.Files.Where(file => file.Outcome == GeminiOutcome.DroppedRight).Select(file => (score.Case.Name, file))))
        {
            Console.WriteLine($"  note: the app's checks drop a RIGHT answer: {drop.Name} / {drop.file.FileName} = E{drop.file.Expected:00} ({drop.file.Reason})");
        }

        var caseRows = probes.Select(probe => (probe, Scorer.ScoreRules(probe))).ToList();
        var report = ReportWriter.Build(
            "Gemini special eval, self-test (fake answers, 0 requests)",
            ["oracle = the right answer for every file; shifted = right answer + 1; silent = no pairs."],
            scores,
            caseRows,
            []);
        if (options.DataDir(required: false) is { } dataDir)
        {
            var path = Path.Combine(dataDir, "reports", "selftest.md");
            SafeFile.WriteAllText(path, report);
            Console.WriteLine($"Report: {path}");
        }

        PrintSummary(scores);
        if (failures.Count == 0)
        {
            Console.WriteLine("Self-test passed.");
            return 0;
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"FAIL: {failure}");
        }

        return 1;

        void Expect(bool condition, string message)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
    }

    private static HttpMessageHandler RehearsalNetwork(string name, IReadOnlyList<CaseProbeResult> probes)
    {
        string Respond(string payload, Func<int, int> pick)
        {
            var id = JsonNode.Parse(payload)?["ctx"]?["id"]?.GetValue<int>();
            var answers = probes
                .Where(probe => probe.Case.TmdbId == id)
                .Select(probe => Answer(probe.Case, payload, pick))
                .ToList();
            return answers.OrderByDescending(answer => answer.Length).FirstOrDefault() ?? "{\"m\":[],\"w\":[]}";
        }

        return name switch
        {
            "oracle" => new ResponderHandler(payload => Respond(payload, expected => expected)),
            "shifted" => new ResponderHandler(payload => Respond(payload, expected => expected + 1)),
            "silent" => new ResponderHandler(_ => "{\"m\":[],\"w\":[]}"),
            "flaky" => new FlakyHandler(new ResponderHandler(payload => Respond(payload, expected => expected)), failEvery: 3),
            _ => throw new ArgumentException("--offline-responder takes oracle, shifted, silent or flaky.")
        };
    }

    /// <summary>Answers a payload with <paramref name="pick"/>(expected) for every file of <paramref name="evalCase"/> it lists.</summary>
    private static string Answer(EvalCase evalCase, string payload, Func<int, int> pick)
    {
        var files = JsonNode.Parse(payload)?["ctx"]?["c"]?.AsArray() ?? [];
        var pairs = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            var folder = files[index]![0]!.GetValue<string>();
            var fileName = files[index]![1]!.GetValue<string>();
            var match = evalCase.ExpectedS00E.FirstOrDefault(pair =>
                pair.Key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) &&
                pair.Key.Split('/') is { Length: >= 2 } parts && parts[^2].Equals(folder, StringComparison.OrdinalIgnoreCase));
            if (match.Value is { } expected)
            {
                pairs.Add($"[{index},{pick(expected)}]");
            }
        }

        return $"{{\"m\":[{string.Join(",", pairs)}],\"w\":[]}}";
    }

    // ---------------------------------------------------------------- shared

    private static IReadOnlyList<EvalCase> LoadCases(Options options, string? dataDir, List<string> notes)
    {
        var all = CaseCatalog.Synthetic()
            .Concat(CaseCatalog.Private(dataDir, options.Has("include-unconfirmed"), notes))
            .ToList();
        var duplicates = all.GroupBy(item => item.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate case names: {string.Join(", ", duplicates)}.");
        }

        return CaseCatalog.Select(all, options.Get("cases"));
    }

    private static IReadOnlyList<PromptVariant> Variants(Options options, bool defaultAll) =>
        options.GetList("prompts", defaultAll ? PromptVariant.KnownNames.ToArray() : [PromptVariant.LiveName])
            .Select(PromptVariant.Get)
            .DistinctBy(variant => variant.Name)
            .ToList();

    private static List<PlannedSample> BuildPlan(Options options, string dataDir, List<string> notes, out IReadOnlyList<CaseProbeResult> probes)
    {
        var models = options.GetList("models");
        if (models.Count == 0)
        {
            throw new ArgumentException("Pass --models <id>[,<id>…] (catalog ids, e.g. gemini-3.5-flash-lite).");
        }

        foreach (var model in models.Where(model => QuotaLedger.Classify(model) == QuotaClass.Unknown))
        {
            throw new ArgumentException($"'{model}' has no known free-tier limit in the app's catalog; the eval does not send to it.");
        }

        var variants = Variants(options, defaultAll: false);
        var samples = Math.Max(1, options.GetInt("samples") ?? 1);
        probes = LoadCases(options, dataDir, notes).Select(CaseProbe.Probe).ToList();
        foreach (var probe in probes.Where(probe => !probe.CallsGemini))
        {
            notes.Add($"{probe.Case.Name}: the rules place every special; the app sends no request (rules tier only).");
        }

        var plan = new List<PlannedSample>();
        for (var sample = 1; sample <= samples; sample++)
        {
            foreach (var probe in probes.Where(probe => probe.CallsGemini))
            {
                foreach (var model in models)
                {
                    plan.AddRange(variants.Select(variant => new PlannedSample(probe, model, variant, sample)));
                }
            }
        }

        return plan;
    }

    private static void PrintPlan(IReadOnlyList<PlannedSample> plan, QuotaLedger ledger)
    {
        Console.WriteLine($"Plan: {plan.Count} request(s) (one per sample; a transient failure may add one retry each), Pacific day {ledger.PacificDay}.");
        foreach (var group in plan.GroupBy(item => item.Model, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {group.Key,-26} {group.Count(),3} planned, {ledger.Remaining(group.Key),3} left today ({QuotaLedger.Classify(group.Key)})");
        }
    }

    private static string? CheckBudget(IReadOnlyList<PlannedSample> plan, QuotaLedger ledger)
    {
        var large = plan.Count(item => QuotaLedger.Classify(item.Model) == QuotaClass.Large);
        if (large > ledger.LargeRemaining())
        {
            return $"The plan needs {large} requests on 500/day models, but only {ledger.LargeRemaining()} are left of the eval's {QuotaLedger.LargeSharedCap}/day.";
        }

        foreach (var group in plan.GroupBy(item => item.Model, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > ledger.Remaining(group.Key))
            {
                return $"The plan needs {group.Count()} requests on {group.Key}, but only {ledger.Remaining(group.Key)} are left today.";
            }
        }

        return null;
    }

    private static void PrintSummary(IReadOnlyList<SampleScore> scores)
    {
        Console.WriteLine();
        Console.WriteLine($"{"model",-26} {"prompt",-6} {"samples",7} {"correct",7} {"WRONG",5} {"drop✓",5} {"drop✗",5} {"left",4} {"abst✓",5} {"err",4}");
        foreach (var cell in ReportWriter.BuildCells(scores))
        {
            Console.WriteLine(
                $"{cell.Model,-26} {cell.Variant,-6} {cell.Samples,7} {cell.Count(GeminiOutcome.Correct),7} {cell.Count(GeminiOutcome.Wrong),5} " +
                $"{cell.Count(GeminiOutcome.DroppedRight),5} {cell.Count(GeminiOutcome.DroppedWrong),5} {cell.Count(GeminiOutcome.LeftOut),4} " +
                $"{cell.Count(GeminiOutcome.AbstainCorrect),5} {cell.ErrorSamples,4}");
        }
    }

    private static void PrintNotes(IReadOnlyList<string> notes)
    {
        foreach (var note in notes)
        {
            Console.WriteLine($"  note: {note}");
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    private static string RecordSafe(string value) => SampleRecord.Sanitize(value);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}

/// <summary>Which harness code produced a report (commit of the repo the tool was built from, if git answers fast).</summary>
internal static class GitInfo
{
    private static string? _cached;

    public static string Describe()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            var repo = SafeFile.FindGitWorkTree(AppContext.BaseDirectory);
            if (repo is null)
            {
                return _cached = "commit unknown";
            }

            var start = new ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return _cached = "commit unknown";
            }

            var commit = process.StandardOutput.ReadToEnd().Trim();
            using var status = Process.Start(new ProcessStartInfo("git", "status --porcelain --untracked-files=no")
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
            var dirty = status.WaitForExit(5000) && status.StandardOutput.ReadToEnd().Trim().Length > 0;
            return _cached = $"commit {commit}{(dirty ? "+dirty" : string.Empty)}";
        }
        catch
        {
            return _cached = "commit unknown";
        }
    }
}
