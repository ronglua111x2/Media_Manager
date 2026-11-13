using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace SearchBench;

/// <summary>
/// SearchBench reeval-shows --dump shows.tsv [--state D:\MediaManagerState] [--backend native] [--kind pack|episode] [--out verdicts.tsv]
///
/// Offline counterpart of reeval-movies for shows-compare dumps (no network): re-evaluates every row with the current
/// rules (episodes with CandidateEvaluationService, packs with SeasonPackIdentity) and prints the rows whose verdict
/// changed, grouped by target. The dump must start with a "show" column (shows-compare writes it since 2026-11-13).
/// Rows without a size column get size 0 (size filters skip it) and a fake magnet; flips from old size verdicts are
/// ignored.
/// </summary>
public static class ReevalShowsCommand
{
    public static int Run(Dictionary<string, string> args)
    {
        var state = args.GetValueOrDefault("state") ?? @"D:\MediaManagerState";
        var dumpPath = args.GetValueOrDefault("dump") ?? throw new ArgumentException("--dump is required");
        var backend = args.GetValueOrDefault("backend");
        var kind = args.GetValueOrDefault("kind");
        // --out file.tsv: every row with its new verdict (to diff two rule versions).
        using var output = args.GetValueOrDefault("out") is { } outPath ? new StreamWriter(outPath, append: false) : null;
        var recipes = Directory.EnumerateFiles(Path.Combine(state, "Recipes"), "*.rcp")
            .Select(path => JsonSerializer.Deserialize<SearchRecipe>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            })!)
            .ToList();
        var shows = ShowsCompareCommand.LoadShows(state).ToDictionary(pair => pair.Show.Title, StringComparer.Ordinal);
        var evaluator = new CandidateEvaluationService(new SearchTitleResolver());
        var targets = new Dictionary<(string Show, string Label), ShowsCompareCommand.Target?>();
        var changes = new List<(string Target, string Old, string New, string Detail, string Name)>();
        var total = 0;
        var accepted = (Old: 0, New: 0);
        var lines = File.ReadLines(dumpPath).ToList();
        var header = lines[0].Split('\t');
        var sizeColumn = Array.IndexOf(header, "size");
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split('\t');
            if (cells.Length < 8 || (backend is not null && cells[2] != backend))
            {
                continue;
            }

            if (!targets.TryGetValue((cells[0], cells[1]), out var target))
            {
                target = ResolveTarget(recipes, shows, cells[0], cells[1]);
                targets[(cells[0], cells[1])] = target;
                if (target is null)
                {
                    Console.Error.WriteLine($"  no target for {cells[0]} {cells[1]}");
                }
            }

            if (target is null ||
                (kind == "pack" && target.Kind != MediaKind.TvSeasonPack) ||
                (kind == "episode" && target.Kind == MediaKind.TvSeasonPack))
            {
                continue;
            }

            var name = cells[^1];
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
            var row = new TorrentSearchResult
            {
                FileName = name,
                FileUrl = $"magnet:?xt=urn:btih:{hash}",
                FileSize = sizeColumn > 0 && long.TryParse(cells[sizeColumn], out var size) ? size : 0,
                Seeders = int.TryParse(cells[5], out var seeds) ? seeds : 0,
                // Native sources without swarm counts (NS-21); the dump does not carry the flag.
                SeedersUnknown = cells[2] == "native" && seeds == 0 && cells[6] is "bt4g" or "tsukihime" or "subsplease",
                EngineName = cells[6]
            };
            var result = ShowsCompareCommand.EvaluateRow(target, evaluator, row);
            var verdict = result.IsAccepted ? $"accepted:{CandidateTiers.Classify(target.Recipe, result)}" : $"rejected:{result.RejectReason}";
            total++;
            output?.WriteLine($"{cells[0]}	{cells[1]}	{cells[2]}	{verdict}	{(result.IsAccepted ? "" : result.RejectDetail)}	{cells[5]}	{cells[6]}	{name}");
            accepted = (accepted.Old + (cells[3].StartsWith("accepted", StringComparison.Ordinal) ? 1 : 0), accepted.New + (result.IsAccepted ? 1 : 0));
            if (!string.Equals(Simplify(verdict), Simplify(cells[3]), StringComparison.Ordinal) &&
                !(sizeColumn < 0 && cells[3].Contains("SizeToo", StringComparison.Ordinal)))
            {
                changes.Add(($"{cells[0]} {cells[1]}", cells[3], verdict, result.IsAccepted ? string.Empty : result.RejectDetail, $"{name} ({cells[6]}, {cells[5]} seeds)"));
            }
        }

        Console.WriteLine($"{total} rows re-evaluated, {changes.Count} changed verdict; accepted {accepted.Old} -> {accepted.New}.");
        foreach (var group in changes.GroupBy(change => change.Target))
        {
            Console.WriteLine();
            Console.WriteLine($"## {group.Key} ({group.Count()})");
            foreach (var change in group.OrderBy(change => change.New).ThenBy(change => change.Name))
            {
                Console.WriteLine($"- {change.Old} -> {change.New} {change.Detail} | {change.Name}");
            }
        }

        return 0;
    }

    private static ShowsCompareCommand.Target? ResolveTarget(
        List<SearchRecipe> recipes,
        Dictionary<string, (TrackedShow Show, List<TrackedEpisode> Episodes)> shows,
        string showTitle,
        string label)
    {
        if (!shows.TryGetValue(showTitle, out var entry))
        {
            return null;
        }

        var pack = System.Text.RegularExpressions.Regex.Match(label, @"^S(?<s>\d+) pack$");
        if (pack.Success)
        {
            return ShowsCompareCommand.PackTarget(recipes, entry.Show, int.Parse(pack.Groups["s"].Value));
        }

        var episode = System.Text.RegularExpressions.Regex.Match(label, @"^S(?<s>\d+)E(?<e>\d+)$");
        if (!episode.Success)
        {
            return null;
        }

        var tracked = entry.Episodes.FirstOrDefault(ep =>
            ep.SeasonNumber == int.Parse(episode.Groups["s"].Value) && ep.EpisodeNumber == int.Parse(episode.Groups["e"].Value));
        return tracked is null ? null : ShowsCompareCommand.EpisodeTarget(recipes, entry.Show, tracked);
    }

    // Accepted:Good vs Accepted:Accepted can flip on seeds alone; compare accepted/rejected + reason.
    private static string Simplify(string verdict) => verdict.StartsWith("accepted", StringComparison.Ordinal) ? "accepted" : verdict;
}
