using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace SearchBench;

/// <summary>
/// SearchBench reeval-movies --dump movies.tsv [--state D:\MediaManagerState] [--backend native]
///
/// Offline: re-evaluates the rows of a movies-compare --dump file with the current rules (no network) and prints
/// every row whose verdict changed, grouped by movie. Used to check a matching change on real release names
/// before keeping it (e.g. the same-year title check). Size and URL are not in the dump: size 0, a fake magnet.
/// </summary>
public static class ReevalMoviesCommand
{
    public static int Run(Dictionary<string, string> args)
    {
        var state = args.GetValueOrDefault("state") ?? @"D:\MediaManagerState";
        var dumpPath = args.GetValueOrDefault("dump") ?? throw new ArgumentException("--dump is required");
        var backend = args.GetValueOrDefault("backend");
        var recipes = Directory.EnumerateFiles(Path.Combine(state, "Recipes"), "*.rcp")
            .Select(path => JsonSerializer.Deserialize<SearchRecipe>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            })!)
            .ToList();
        var movies = MoviesCompareCommand.LoadMovies(state).ToDictionary(movie => movie.Title, StringComparer.Ordinal);
        var evaluator = new CandidateEvaluationService(new SearchTitleResolver());
        var changes = new List<(string Movie, string Old, string New, string Detail, string Name)>();
        var total = 0;
        foreach (var line in File.ReadLines(dumpPath).Skip(1))
        {
            var cells = line.Split('\t');
            if (cells.Length < 7 || !movies.TryGetValue(cells[0], out var movie) || (backend is not null && cells[1] != backend))
            {
                continue;
            }

            var recipe = recipes.FirstOrDefault(r => r.RecipeId == movie.RecipeId && r.TargetKind == MediaKind.Movie)
                         ?? recipes.First(r => r.TargetKind == MediaKind.Movie && r.RecipeId.StartsWith("default", StringComparison.OrdinalIgnoreCase));
            var name = cells[6];
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
            var row = new TorrentSearchResult
            {
                FileName = name,
                FileUrl = $"magnet:?xt=urn:btih:{hash}",
                Seeders = int.TryParse(cells[4], out var seeds) ? seeds : 0,
                EngineName = cells[5]
            };
            var result = evaluator.EvaluateMovie(recipe, movie, row);
            var verdict = result.IsAccepted ? $"accepted:{CandidateTiers.Classify(recipe, result)}" : $"rejected:{result.RejectReason}";
            total++;
            // Old SizeTooSmall/junk-size verdicts cannot be reproduced without the size: ignore those flips.
            if (!string.Equals(Simplify(verdict), Simplify(cells[2]), StringComparison.Ordinal) && !cells[2].Contains("SizeToo", StringComparison.Ordinal))
            {
                changes.Add((movie.Title, cells[2], verdict, result.IsAccepted ? string.Empty : result.RejectDetail, name));
            }
        }

        Console.WriteLine($"{total} rows re-evaluated, {changes.Count} changed verdict.");
        foreach (var group in changes.GroupBy(change => change.Movie))
        {
            Console.WriteLine();
            Console.WriteLine($"## {group.Key} ({group.Count()})");
            foreach (var change in group.OrderBy(change => change.New))
            {
                Console.WriteLine($"- {change.Old} -> {change.New} {change.Detail} | {change.Name}");
            }
        }

        return 0;
    }

    // Accepted:Good vs Accepted:Accepted can flip on seeds alone; compare accepted/rejected + reason.
    private static string Simplify(string verdict) => verdict.StartsWith("accepted", StringComparison.Ordinal) ? "accepted" : verdict;
}
