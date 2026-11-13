using System.Globalization;
using System.Text;
using GeminiSpecialEval.Running;

namespace GeminiSpecialEval.Scoring;

/// <summary>Counts for one model × prompt cell.</summary>
internal sealed class CellStats
{
    public CellStats(string model, string variant)
    {
        Model = model;
        Variant = variant;
    }

    public string Model { get; }

    public string Variant { get; }

    public int Samples { get; set; }

    public int ErrorSamples { get; set; }

    public int StaleSamples { get; set; }

    public int Requests { get; set; }

    public int FailedRequests { get; set; }

    public List<long> LatenciesMs { get; } = [];

    public Dictionary<GeminiOutcome, int> Outcomes { get; } = Enum.GetValues<GeminiOutcome>().ToDictionary(item => item, _ => 0);

    public int RawPairs { get; set; }

    public int RawPairsRight { get; set; }

    public int Count(GeminiOutcome outcome) => Outcomes[outcome];

    /// <summary>Files that are real TMDB specials, in samples that got an answer (errors excluded).</summary>
    public int RealSpecials { get; set; }

    public int Applied => Count(GeminiOutcome.Correct) + Count(GeminiOutcome.Wrong);
}

internal static class ReportWriter
{
    public static string Build(
        string title,
        IReadOnlyList<string> headerLines,
        IReadOnlyList<SampleScore> samples,
        IReadOnlyList<(CaseProbeResult Probe, IReadOnlyList<RuleFileScore> Rules)> cases,
        IReadOnlyList<string> notes)
    {
        var scored = samples.Where(sample => !sample.Stale).ToList();
        var cells = BuildCells(samples);
        var text = new StringBuilder();
        text.AppendLine($"# {title}");
        text.AppendLine();
        foreach (var line in headerLines)
        {
            text.AppendLine($"- {line}");
        }

        text.AppendLine();
        text.AppendLine("## Summary (files Gemini was asked about)");
        text.AppendLine();
        text.AppendLine("Wrong = linked to another S00 episode, silently (the number that matters for auto-apply). " +
                        "Dropped = Gemini answered but the app's checks removed the pair (✓ Gemini was right, ✗ it was wrong). " +
                        "Coverage = correct links / real specials asked. Wrong rate = wrong / applied links, with the 95% Wilson upper bound.");
        text.AppendLine();
        text.AppendLine("| Model | Prompt | Samples ok/err | Files | Correct | **Wrong** | Dropped ✓/✗ | Left out | Abstain ✓ | Coverage | Wrong rate (95% ≤) | Raw pairs right | Median latency |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var cell in cells)
        {
            var files = cell.Outcomes.Where(pair => pair.Key != GeminiOutcome.Error).Sum(pair => pair.Value);
            text.AppendLine(
                $"| {cell.Model} | {cell.Variant} | {cell.Samples - cell.ErrorSamples}/{cell.ErrorSamples} | {files} | " +
                $"{cell.Count(GeminiOutcome.Correct)} | **{cell.Count(GeminiOutcome.Wrong)}** | " +
                $"{cell.Count(GeminiOutcome.DroppedRight)}/{cell.Count(GeminiOutcome.DroppedWrong)} | {cell.Count(GeminiOutcome.LeftOut)} | " +
                $"{cell.Count(GeminiOutcome.AbstainCorrect)} | {Percent(cell.Count(GeminiOutcome.Correct), cell.RealSpecials)} | " +
                $"{(cell.Applied == 0 ? "–" : $"{Percent(cell.Count(GeminiOutcome.Wrong), cell.Applied)} ({Percent(WilsonUpper(cell.Count(GeminiOutcome.Wrong), cell.Applied))})")} | " +
                $"{cell.RawPairsRight}/{cell.RawPairs} | {Median(cell.LatenciesMs)} |");
        }

        if (samples.Any(sample => sample.Stale))
        {
            text.AppendLine();
            text.AppendLine($"{samples.Count(sample => sample.Stale)} recorded sample(s) left out of the table: the app now builds a different payload for them (see Replay notes).");
        }

        AppendPerCase(text, scored, cells);
        AppendPaired(text, scored);
        AppendStability(text, scored);
        AppendIssues(text, scored);
        AppendRules(text, cases);
        AppendRequests(text, cells);

        if (notes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Notes");
            text.AppendLine();
            foreach (var note in notes)
            {
                text.AppendLine($"- {note}");
            }
        }

        var replayNotes = samples.Where(sample => sample.ReplayNote is not null).ToList();
        if (replayNotes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Replay notes");
            text.AppendLine();
            foreach (var sample in replayNotes)
            {
                text.AppendLine($"- {sample.Record.Case} / {sample.Record.Model} / {sample.Record.Variant} / s{sample.Record.Sample}: {sample.ReplayNote}");
            }
        }

        return text.ToString();
    }

    public static IReadOnlyList<CellStats> BuildCells(IReadOnlyList<SampleScore> samples)
    {
        var cells = new Dictionary<(string, string), CellStats>();
        foreach (var sample in samples)
        {
            var key = (sample.Record.Model, sample.Record.Variant);
            if (!cells.TryGetValue(key, out var cell))
            {
                cells[key] = cell = new CellStats(sample.Record.Model, sample.Record.Variant);
            }

            cell.Requests += sample.Record.Exchanges.Count(exchange => exchange.ErrorKind != "budget");
            cell.FailedRequests += sample.Record.Exchanges.Count(exchange => exchange.ErrorKind is "timeout" or "network" || exchange.Status is >= 300);
            cell.LatenciesMs.AddRange(sample.Record.Exchanges.Where(exchange => exchange.Status == 200).Select(exchange => exchange.ElapsedMs));
            if (sample.Stale)
            {
                cell.StaleSamples++;
                continue;
            }

            cell.Samples++;
            if (sample.Files.Any(file => file.Outcome == GeminiOutcome.Error))
            {
                cell.ErrorSamples++;
            }

            foreach (var file in sample.Files)
            {
                cell.Outcomes[file.Outcome]++;
                if (file.Expected is not null && file.Outcome != GeminiOutcome.Error)
                {
                    cell.RealSpecials++;
                }
            }

            cell.RawPairs += sample.RawPairs;
            cell.RawPairsRight += sample.RawPairsRight;
        }

        return cells.Values
            .OrderBy(cell => cell.Model, StringComparer.Ordinal)
            .ThenBy(cell => cell.Variant, StringComparer.Ordinal)
            .ToList();
    }

    private static void AppendPerCase(StringBuilder text, IReadOnlyList<SampleScore> samples, IReadOnlyList<CellStats> cells)
    {
        if (samples.Count == 0)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("## Per case");
        text.AppendLine();
        text.AppendLine("Each cell: correct / real specials asked, then ✗ wrong links (on any file, a non-special included), ⊘ dropped, – left out, ! failed samples (summed over samples).");
        text.AppendLine();
        text.Append("| Case | Files to Gemini |");
        foreach (var cell in cells)
        {
            text.Append($" {cell.Model} {cell.Variant} |");
        }

        text.AppendLine();
        text.Append("|---|---|");
        text.AppendLine(string.Concat(cells.Select(_ => "---|")));
        foreach (var group in samples.GroupBy(sample => sample.Case.Name).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var filesSent = group.First().Files.Count;
            text.Append($"| {group.Key} | {filesSent} |");
            foreach (var cell in cells)
            {
                var cellSamples = group.Where(sample => sample.Record.Model == cell.Model && sample.Record.Variant == cell.Variant).ToList();
                text.Append($" {DescribeCaseCell(cellSamples)} |");
            }

            text.AppendLine();
        }
    }

    private static string DescribeCaseCell(IReadOnlyList<SampleScore> samples)
    {
        if (samples.Count == 0)
        {
            return "·";
        }

        var files = samples.SelectMany(sample => sample.Files).ToList();
        int Count(GeminiOutcome outcome) => files.Count(file => file.Outcome == outcome);
        var real = files.Count(file => file.Expected is not null && file.Outcome != GeminiOutcome.Error);
        var parts = new List<string> { $"{Count(GeminiOutcome.Correct)}/{real}" };
        if (Count(GeminiOutcome.Wrong) > 0)
        {
            parts.Add($"✗{Count(GeminiOutcome.Wrong)}");
        }

        var dropped = Count(GeminiOutcome.DroppedRight) + Count(GeminiOutcome.DroppedWrong);
        if (dropped > 0)
        {
            parts.Add($"⊘{dropped}");
        }

        if (Count(GeminiOutcome.LeftOut) > 0)
        {
            parts.Add($"–{Count(GeminiOutcome.LeftOut)}");
        }

        var errors = samples.Count(sample => sample.Files.Any(file => file.Outcome == GeminiOutcome.Error));
        if (errors > 0)
        {
            parts.Add($"!{errors}");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Prompt against prompt on the same request: same run, case, model and sample number. Counts the units where
    /// one prompt made fewer wrong links than the other, with an exact two-sided sign test on the units that differ.
    /// </summary>
    private static void AppendPaired(StringBuilder text, IReadOnlyList<SampleScore> samples)
    {
        var units = samples
            .Where(sample => sample.Files.All(file => file.Outcome != GeminiOutcome.Error))
            .GroupBy(sample => (sample.Record.RunId, sample.Case.Name, sample.Record.Model, sample.Record.Sample))
            .Select(group => group.ToDictionary(sample => sample.Record.Variant, sample => sample.Files.Count(file => file.Outcome == GeminiOutcome.Wrong)))
            .Where(unit => unit.Count >= 2)
            .ToList();
        var variants = units.SelectMany(unit => unit.Keys).Distinct().Order(StringComparer.Ordinal).ToList();
        if (units.Count == 0 || variants.Count < 2)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("## Prompts compared on the same requests");
        text.AppendLine();
        text.AppendLine("A unit is one run × case × model × sample answered under both prompts. Sign test: exact, two-sided, on the units where the wrong counts differ.");
        text.AppendLine();
        text.AppendLine("| A | B | Units | A fewer wrong | Same | B fewer wrong | Wrong links A → B | Sign test p |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");
        for (var i = 0; i < variants.Count; i++)
        {
            for (var j = i + 1; j < variants.Count; j++)
            {
                var (a, b) = (variants[i], variants[j]);
                var pairs = units.Where(unit => unit.ContainsKey(a) && unit.ContainsKey(b)).Select(unit => (A: unit[a], B: unit[b])).ToList();
                var aBetter = pairs.Count(pair => pair.A < pair.B);
                var bBetter = pairs.Count(pair => pair.B < pair.A);
                text.AppendLine(
                    $"| {a} | {b} | {pairs.Count} | {aBetter} | {pairs.Count - aBetter - bBetter} | {bBetter} | " +
                    $"{pairs.Sum(pair => pair.A)} → {pairs.Sum(pair => pair.B)} | {SignTest(aBetter, bBetter).ToString("0.000", CultureInfo.InvariantCulture)} |");
            }
        }
    }

    /// <summary>Exact two-sided sign test p-value for <paramref name="wins"/> against <paramref name="losses"/> (ties dropped).</summary>
    public static double SignTest(int wins, int losses)
    {
        var n = wins + losses;
        if (n == 0)
        {
            return 1;
        }

        var tail = 0.0;
        for (var k = 0; k <= Math.Min(wins, losses); k++)
        {
            tail += Math.Exp(LogChoose(n, k) - (n * Math.Log(2)));
        }

        return Math.Min(1, 2 * tail);
    }

    private static double LogChoose(int n, int k)
    {
        var sum = 0.0;
        for (var i = 1; i <= k; i++)
        {
            sum += Math.Log(n - k + i) - Math.Log(i);
        }

        return sum;
    }

    private static void AppendStability(StringBuilder text, IReadOnlyList<SampleScore> samples)
    {
        var groups = samples
            .GroupBy(sample => (sample.Case.Name, sample.Record.Model, sample.Record.Variant))
            .Where(group => group.Count(sample => sample.Files.All(file => file.Outcome != GeminiOutcome.Error)) > 1)
            .ToList();
        if (groups.Count == 0)
        {
            return;
        }

        var unstable = groups
            .Where(group => group
                .Where(sample => sample.Files.All(file => file.Outcome != GeminiOutcome.Error))
                .Select(sample => string.Join(";", sample.Files.Select(file => $"{file.RelativePath}={file.Final}")))
                .Distinct(StringComparer.Ordinal)
                .Count() > 1)
            .Select(group => $"{group.Key.Name} / {group.Key.Model} / {group.Key.Variant}")
            .ToList();
        text.AppendLine();
        text.AppendLine("## Stability (same input, several samples)");
        text.AppendLine();
        text.AppendLine($"{groups.Count - unstable.Count} of {groups.Count} case × model × prompt groups gave the same final mapping every time.");
        foreach (var item in unstable)
        {
            text.AppendLine($"- differs between samples: {item}");
        }
    }

    private static void AppendIssues(StringBuilder text, IReadOnlyList<SampleScore> samples)
    {
        var wrong = samples
            .SelectMany(sample => sample.Files.Where(file => file.Outcome == GeminiOutcome.Wrong).Select(file => (sample, file)))
            .ToList();
        text.AppendLine();
        text.AppendLine("## Wrong links (critical)");
        text.AppendLine();
        if (wrong.Count == 0)
        {
            text.AppendLine("None.");
        }
        else
        {
            text.AppendLine("| Model | Prompt | Case | s | File | Expected | Linked to | Gemini said |");
            text.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var (sample, file) in wrong)
            {
                text.AppendLine($"| {sample.Record.Model} | {sample.Record.Variant} | {sample.Case.Name} | {sample.Record.Sample} | {Cell(file.FileName)} | {Episode(file.Expected)} | {Episode(file.Final)} | {string.Join(", ", file.Raw.Select(item => Episode(item)))} |");
            }
        }

        var dropped = samples
            .SelectMany(sample => sample.Files
                .Where(file => file.Outcome is GeminiOutcome.DroppedRight or GeminiOutcome.DroppedWrong)
                .Select(file => (sample, file)))
            .ToList();
        text.AppendLine();
        text.AppendLine("## Dropped by the app's checks");
        text.AppendLine();
        if (dropped.Count == 0)
        {
            text.AppendLine("None.");
        }
        else
        {
            text.AppendLine("| Model | Prompt | Case | s | File | Expected | Gemini said | Gemini right? | Why dropped |");
            text.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var (sample, file) in dropped)
            {
                text.AppendLine($"| {sample.Record.Model} | {sample.Record.Variant} | {sample.Case.Name} | {sample.Record.Sample} | {Cell(file.FileName)} | {Episode(file.Expected)} | {string.Join(", ", file.Raw.Select(item => Episode(item)))} | {(file.Outcome == GeminiOutcome.DroppedRight ? "yes (false alarm)" : "no (saved)")} | {Cell(file.Reason ?? string.Empty)} |");
            }
        }

        var errors = samples.Where(sample => sample.Files.Any(file => file.Outcome == GeminiOutcome.Error)).ToList();
        if (errors.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Failed samples");
            text.AppendLine();
            text.AppendLine("| Model | Prompt | Case | s | HTTP | Error |");
            text.AppendLine("|---|---|---|---|---|---|");
            foreach (var sample in errors)
            {
                var statuses = string.Join(", ", sample.Record.Exchanges.Select(exchange => exchange.Status?.ToString(CultureInfo.InvariantCulture) ?? exchange.ErrorKind ?? "?"));
                text.AppendLine($"| {sample.Record.Model} | {sample.Record.Variant} | {sample.Case.Name} | {sample.Record.Sample} | {statuses} | {Cell(Truncate(sample.Record.Error ?? "unparseable answer", 160))} |");
            }
        }
    }

    private static void AppendRules(StringBuilder text, IReadOnlyList<(CaseProbeResult Probe, IReadOnlyList<RuleFileScore> Rules)> cases)
    {
        if (cases.Count == 0)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("## Rules tier (no Gemini; identical for every model and prompt)");
        text.AppendLine();
        text.AppendLine("| Case | Source | Tags | Specials | Rule-placed | Rule correct | **Rule wrong** | Rule unmapped | Not detected | Sent to Gemini |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var (probe, rules) in cases)
        {
            int Count(RuleOutcome outcome) => rules.Count(rule => rule.Outcome == outcome);
            var placed = Count(RuleOutcome.Correct) + Count(RuleOutcome.Wrong);
            text.AppendLine(
                $"| {probe.Case.Name} | {probe.Case.Source} | {string.Join(", ", probe.Case.Tags)} | {probe.Case.ExpectedS00E.Count} | {placed} | " +
                $"{Count(RuleOutcome.Correct)} | **{Count(RuleOutcome.Wrong)}** | {Count(RuleOutcome.Unmapped)} | {Count(RuleOutcome.NotDetected)} | {Count(RuleOutcome.SentToGemini)} |");
        }

        var ruleIssues = cases
            .SelectMany(item => item.Rules
                .Where(rule => rule.Outcome is RuleOutcome.Wrong or RuleOutcome.NotDetected ||
                               (rule.Outcome == RuleOutcome.Unmapped && rule.Expected is not null))
                .Select(rule => (item.Probe.Case.Name, rule)))
            .ToList();
        if (ruleIssues.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("| Case | File | Expected | Rules gave | Outcome | Reason |");
            text.AppendLine("|---|---|---|---|---|---|");
            foreach (var (name, rule) in ruleIssues)
            {
                text.AppendLine($"| {name} | {Cell(rule.FileName)} | {Episode(rule.Expected)} | {Episode(rule.Final)} | {rule.Outcome} | {Cell(rule.Reason ?? string.Empty)} |");
            }
        }
    }

    private static void AppendRequests(StringBuilder text, IReadOnlyList<CellStats> cells)
    {
        text.AppendLine();
        text.AppendLine("## Requests in these samples");
        text.AppendLine();
        text.AppendLine("| Model | Requests | Failed (HTTP ≥ 300, timeout, network) |");
        text.AppendLine("|---|---|---|");
        foreach (var model in cells.GroupBy(cell => cell.Model))
        {
            text.AppendLine($"| {model.Key} | {model.Sum(cell => cell.Requests)} | {model.Sum(cell => cell.FailedRequests)} |");
        }
    }

    /// <summary>Upper end of the 95% Wilson score interval for <paramref name="hits"/> out of <paramref name="total"/>.</summary>
    public static double WilsonUpper(int hits, int total)
    {
        if (total == 0)
        {
            return 1;
        }

        const double z = 1.959964;
        var p = (double)hits / total;
        var denominator = 1 + (z * z / total);
        var centre = p + (z * z / (2 * total));
        var margin = z * Math.Sqrt((p * (1 - p) / total) + (z * z / (4.0 * total * total)));
        return Math.Min(1, (centre + margin) / denominator);
    }

    private static string Percent(int part, int total) => total == 0 ? "–" : Percent((double)part / total);

    private static string Percent(double value) => value.ToString("0.0%", CultureInfo.InvariantCulture);

    private static string Median(List<long> values)
    {
        if (values.Count == 0)
        {
            return "–";
        }

        var ordered = values.Order().ToList();
        var middle = ordered[ordered.Count / 2];
        return $"{middle / 1000.0:0.0} s";
    }

    private static string Episode(int? number) => number is null ? "—" : $"E{number:00}";

    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
