using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Search;
using media_management_app.Services;
using Microsoft.Data.Sqlite;

namespace SearchBench;

/// <summary>
/// SearchBench movies-compare [--state D:\MediaManagerState] [--only "a,b"] [--plugins …] [--max-seconds 45]
///
/// For every tracked movie, runs the app's query plan on BOTH backends (qBittorrent search and native indexers),
/// evaluates every row with the app's rules and counts candidates per tier (Accepted / Good / Excellent,
/// native-search plan 08 §Q1). Reports whether the #1 candidate is the same, the score gap, and examples of
/// Good candidates only one backend found (to see which sources native is missing).
/// </summary>
public static class MoviesCompareCommand
{
    public static async Task<int> RunAsync(Dictionary<string, string> args)
    {
        var state = args.GetValueOrDefault("state") ?? @"D:\MediaManagerState";
        var only = (args.GetValueOrDefault("only") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var plugins = args.GetValueOrDefault("plugins") ?? "limetorrents|thepiratebay|torrentdownloads|yts|nyaasi|nekobt|sktorrent|kickasstorrents";
        var maxSeconds = int.TryParse(args.GetValueOrDefault("max-seconds"), out var ms) ? ms : 45;
        // --native-only 1: skip qBittorrent (fast check of matching changes). --dump <file>: one TSV line per
        // unique row with the verdict (accepted + tier, or reject reason + detail).
        var nativeOnly = args.GetValueOrDefault("native-only") is "1" or "true";
        var dumpPath = args.GetValueOrDefault("dump");
        using var dump = dumpPath is null ? null : new StreamWriter(dumpPath, append: false);
        dump?.WriteLine("movie\tbackend\tverdict\tdetail\tseeds\tengine\tname");

        var recipes = Directory.EnumerateFiles(Path.Combine(state, "Recipes"), "*.rcp")
            .Select(path => JsonSerializer.Deserialize<SearchRecipe>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            })!)
            .ToList();
        var autoTorrent = LoadAutoTorrent(state);
        var movies = LoadMovies(state)
            .Where(movie => only.Length == 0 || only.Any(o => movie.Title.Contains(o, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var planner = new SearchPlanBuilder(new SearchTitleResolver());
        var evaluator = new CandidateEvaluationService(new SearchTitleResolver());
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
        // Shares the app's indexer budget (07d GOV-07); one search session per target, as the app does (GOV-03 B).
        using var benchState = BenchState.Open(args);
        var governor = benchState.Governor;
        var cache = new IndexerResponseCache();
        var session = new SearchSession();
        var orchestrator = new SearchOrchestrator(governor, (id, lane, budget) =>
            new GovernedIndexerHttp(http, governor, cache, id, lane, budget.MaxGovernorWait, TimeSpan.FromMinutes(30), "MediaManager/1.0 (+native-search; SearchBench)", budget.PerIndexer, session));
        var indexers = NativeIndexerCatalog.Create();
        using var qbit = new QbittorrentSearchBackend();
        if (!nativeOnly)
        {
            await qbit.InitAsync(CancellationToken.None);
        }

        Console.WriteLine("| Movie | Recipe | qBit acc/good/exc | Native acc/good/exc | Same #1 | #1 score qBit → native | Good only in qBit | Good only in native | qBit s | Native s |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
        var examples = new List<string>();
        foreach (var movie in movies)
        {
            var recipe = recipes.FirstOrDefault(r => r.RecipeId == movie.RecipeId && r.TargetKind == MediaKind.Movie)
                         ?? recipes.First(r => r.TargetKind == MediaKind.Movie && r.RecipeId.StartsWith("default", StringComparison.OrdinalIgnoreCase));
            var queries = planner.BuildMovieQueries(recipe, movie);
            var anime = RecipeRuntimeSettings.UsesAnimeAbsoluteEpisodeNumbering(recipe);

            var qbitWatch = Stopwatch.StartNew();
            var qbitRows = new List<TorrentSearchResult>();
            foreach (var query in nativeOnly ? [] : queries)
            {
                qbitRows.AddRange((await qbit.SearchAsync(query, plugins, maxSeconds, CancellationToken.None)).Rows);
            }

            qbitWatch.Stop();

            var nativeWatch = Stopwatch.StartNew();
            var nativeRows = new List<TorrentSearchResult>();
            session = new SearchSession();
            var qualities = RecipeCandidateFilter.GetFilterModule(recipe)?.QualityAllowList;
            var nativeQueries = queries
                .SelectMany(query => NativeQueryText.SpecificQueries(query, MediaKind.Movie, qualities, movie.ReleaseYear)
                    .Select(specific => new IndexerQuery(specific, MediaKind.Movie, anime, SpecificOnly: true))
                    .Prepend(new IndexerQuery(NativeQueryText.Reduce(query, MediaKind.Movie, movie.ReleaseYear), MediaKind.Movie, anime)))
                .DistinctBy(query => (query.Text.ToLowerInvariant(), query.SpecificOnly))
                .ToList();
            foreach (var nativeQuery in nativeQueries)
            {
                var reduced = nativeQuery.Text;
                var outcome = await orchestrator.SearchAsync([nativeQuery], indexers, SearchLane.Interactive, SearchBudget.Interactive, CancellationToken.None);
                nativeRows.AddRange(outcome.Results);
                Console.Error.WriteLine($"  native '{reduced}': {outcome.Summary}");
            }

            nativeWatch.Stop();

            var aliases = SearchIdentity.BuildAliases(nativeRows);
            string Key(TorrentSearchResult row) => SearchIdentity.Key(row, aliases);
            if (dump is not null)
            {
                foreach (var (backend, rows) in new[] { ("native", nativeRows), ("qbittorrent", qbitRows) })
                {
                    foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.FileUrl)).GroupBy(Key).Select(group => group.OrderByDescending(row => row.Seeders).First()))
                    {
                        var e = evaluator.EvaluateMovie(recipe, movie, row);
                        var verdict = e.IsAccepted ? $"accepted:{CandidateTiers.Classify(recipe, e)}" : $"rejected:{e.RejectReason}";
                        dump.WriteLine($"{movie.Title}\t{backend}\t{verdict}\t{(e.IsAccepted ? "" : e.RejectDetail)}\t{row.Seeders}\t{row.EngineName}\t{row.FileName.Replace('\t', ' ')}");
                    }
                }
            }
            var q = Tiers(recipe, movie, qbitRows, evaluator, autoTorrent, Key);
            var n = Tiers(recipe, movie, nativeRows, evaluator, autoTorrent, Key);
            var qTop = q.Accepted.OrderByDescending(r => r.TotalScore).FirstOrDefault();
            var nTop = n.Accepted.OrderByDescending(r => r.TotalScore).FirstOrDefault();
            var sameTop = qTop is not null && nTop is not null &&
                          Key(qTop.SearchResult) == Key(nTop.SearchResult);
            var qGood = q.Good.GroupBy(r => Key(r.SearchResult)).ToDictionary(g => g.Key, g => g.First());
            var nGood = n.Good.GroupBy(r => Key(r.SearchResult)).ToDictionary(g => g.Key, g => g.First());
            var onlyQ = qGood.Keys.Except(nGood.Keys).ToList();
            var onlyN = nGood.Keys.Except(qGood.Keys).ToList();
            Console.WriteLine(
                $"| {movie.Title} | {recipe.Name} | {q.Accepted.Count}/{q.Good.Count}/{q.Excellent} | {n.Accepted.Count}/{n.Good.Count}/{n.Excellent} | " +
                $"{(qTop is null || nTop is null ? "-" : sameTop ? "yes" : "no")} | {qTop?.TotalScore.ToString() ?? "-"} → {nTop?.TotalScore.ToString() ?? "-"} | " +
                $"{onlyQ.Count} | {onlyN.Count} | {qbitWatch.Elapsed.TotalSeconds:0} | {nativeWatch.Elapsed.TotalSeconds:0} |");
            foreach (var key in onlyQ.Take(3))
            {
                var nativeRow = nativeRows.FirstOrDefault(row => Key(row) == key);
                string why;
                if (nativeRow is null)
                {
                    why = "native: no such torrent";
                }
                else
                {
                    var e = evaluator.EvaluateMovie(recipe, movie, nativeRow);
                    why = e.IsAccepted
                        ? $"native has it: accepted, tier {CandidateTiers.Classify(recipe, e)}, seeds {nativeRow.Seeders}, engine {nativeRow.EngineName}, name '{nativeRow.FileName}'"
                        : $"native has it: rejected {e.RejectReason} {e.RejectDetail}";
                }
                examples.Add($"- {movie.Title}: only qBittorrent: {qGood[key].SearchResult.FileName} ({qGood[key].SearchResult.EngineName}, {qGood[key].SearchResult.Seeders} seeds) | {why}");
            }
        }

        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Good candidates only qBittorrent found (what native is missing):");
            examples.ForEach(Console.WriteLine);
        }

        return 0;
    }

    private static (List<RecipeCandidateResult> Accepted, List<RecipeCandidateResult> Good, int Excellent) Tiers(
        SearchRecipe recipe,
        TrackedMovie movie,
        IEnumerable<TorrentSearchResult> rows,
        CandidateEvaluationService evaluator,
        AutoTorrentSettings autoTorrent,
        Func<TorrentSearchResult, string> key)
    {
        var unique = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.FileUrl))
            .GroupBy(key)
            .Select(group => group.OrderByDescending(row => row.Seeders).First());
        var accepted = unique.Select(row => evaluator.EvaluateMovie(recipe, movie, row)).Where(r => r.IsAccepted).ToList();
        if (RecipeRuntimeSettings.GetDeduplicateCandidates(recipe, autoTorrent))
        {
            accepted = CandidateDeduplicator.Deduplicate(
                accepted.OrderByDescending(r => r.TotalScore), r => r.SearchResult.FileName, r => r.SearchResult.FileSize,
                r => r.SearchResult.FileUrl, r => r.SearchResult.Seeders,
                RecipeRuntimeSettings.GetFuzzyDeduplicate(recipe, autoTorrent),
                RecipeRuntimeSettings.GetFuzzyDeduplicateSizeToleranceMb(recipe, autoTorrent));
        }

        var tiers = accepted.Select(r => (Result: r, Tier: CandidateTiers.Classify(recipe, r))).ToList();
        return (accepted, tiers.Where(t => t.Tier >= CandidateTier.Good).Select(t => t.Result).ToList(), tiers.Count(t => t.Tier == CandidateTier.Excellent));
    }

    internal static AutoTorrentSettings LoadAutoTorrent(string state)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(state, "settings.json")));
        return doc.RootElement.TryGetProperty("AutoTorrent", out var at)
            ? JsonSerializer.Deserialize<AutoTorrentSettings>(at.GetRawText()) ?? new AutoTorrentSettings()
            : new AutoTorrentSettings();
    }

    internal static List<TrackedMovie> LoadMovies(string state)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(state, "media-manager.db")};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "select Id, TmdbId, Title, ReleaseYear, AlternativeTitlesJson, ExcludedAlternativeTitlesJson, RecipeId from TrackedMovies order by ReleaseYear";
        using var reader = command.ExecuteReader();
        var movies = new List<TrackedMovie>();
        while (reader.Read())
        {
            movies.Add(new TrackedMovie
            {
                Id = reader.GetInt64(0),
                TmdbId = reader.GetInt32(1),
                Title = reader.GetString(2),
                ReleaseYear = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                AlternativeTitlesJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                ExcludedAlternativeTitlesJson = reader.IsDBNull(5) ? null : reader.GetString(5),
                RecipeId = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return movies;
    }
}
