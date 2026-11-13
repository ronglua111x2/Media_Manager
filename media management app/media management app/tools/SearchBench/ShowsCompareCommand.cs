using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Search;
using media_management_app.Services;
using Microsoft.Data.Sqlite;

namespace SearchBench;

/// <summary>
/// SearchBench shows-compare [--state D:\MediaManagerState] [--only "a,b"] [--episodes 1] [--packs 1]
///                           [--max-seconds 30] [--native-only 1] [--dump file.tsv] [--plugins …] [--disable "a,b"]
///
/// TV / anime counterpart of movies-compare (audit 07c NS-31). For every tracked show it takes the latest aired
/// episode(s) and the latest fully aired season, builds the app's queries with the show's own episode / pack
/// recipe, searches BOTH backends (qBittorrent search with the recipe's plugins, native indexers with the same
/// reduced/specific queries the app sends, IMDb id for EZTV) and evaluates every row with the app's rules:
/// episodes with CandidateEvaluationService.EvaluateEpisode, packs with SeasonPackIdentity (no metadata probe:
/// the bench never adds torrents). Candidates are counted per tier (Accepted / Good / Excellent).
/// Note: recipes using show-snapshot search are measured with their per-episode queries (same queries to both
/// backends), not with the snapshot's show-level queries.
/// </summary>
public static class ShowsCompareCommand
{
    private static readonly string[] AnimeEngines = ["nyaasi", "subsplease", "nekobt", "tsukihime", "animetosho"];

    internal sealed record Target(TrackedShow Show, MediaKind Kind, SearchRecipe Recipe, TrackedEpisode? Episode, int? Season, string Label);

    public static async Task<int> RunAsync(Dictionary<string, string> args)
    {
        var state = args.GetValueOrDefault("state") ?? @"D:\MediaManagerState";
        var only = (args.GetValueOrDefault("only") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var episodesPerShow = int.TryParse(args.GetValueOrDefault("episodes"), out var e) ? Math.Max(0, e) : 1;
        var packs = args.GetValueOrDefault("packs") is not ("0" or "false");
        var maxSeconds = int.TryParse(args.GetValueOrDefault("max-seconds"), out var ms) ? ms : 30;
        var pluginsOverride = args.GetValueOrDefault("plugins");
        var nativeOnly = args.GetValueOrDefault("native-only") is "1" or "true";
        var dumpPath = args.GetValueOrDefault("dump");
        using var dump = dumpPath is null ? null : new StreamWriter(dumpPath, append: false);
        dump?.WriteLine("show\ttarget\tbackend\tverdict\tdetail\tseeds\tengine\tsize\tname");

        var recipes = Directory.EnumerateFiles(Path.Combine(state, "Recipes"), "*.rcp")
            .Select(path => JsonSerializer.Deserialize<SearchRecipe>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            })!)
            .ToList();
        var autoTorrent = MoviesCompareCommand.LoadAutoTorrent(state);
        var targets = LoadTargets(state, recipes, episodesPerShow, packs)
            .Where(target => only.Length == 0 || only.Any(o => target.Show.Title.Contains(o, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var planner = new SearchPlanBuilder(new SearchTitleResolver());
        var evaluator = new CandidateEvaluationService(new SearchTitleResolver());
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
        var governor = new IndexerRequestGovernor();
        var cache = new IndexerResponseCache();
        var orchestrator = new SearchOrchestrator(governor, (id, lane, budget) =>
            new GovernedIndexerHttp(http, governor, cache, id, lane, budget.MaxGovernorWait, TimeSpan.FromMinutes(30), "MediaManager/1.0 (+native-search; SearchBench)", budget.PerIndexer));
        // --disable "a,b": leave indexers out (e.g. ones whose daily budget an earlier run already used today).
        var disabled = (args.GetValueOrDefault("disable") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nativeSettings = new NativeSearchSettings();
        foreach (var id in disabled)
        {
            nativeSettings.Indexers[id] = new IndexerSettings { Enabled = false };
        }

        var indexers = NativeIndexerCatalog.Create(nativeSettings);
        var imdb = new ImdbResolver(state);
        using var qbit = new QbittorrentSearchBackend();
        if (!nativeOnly)
        {
            await qbit.InitAsync(CancellationToken.None);
        }

        Console.WriteLine("| Show | Target | Recipe | qBit acc/good/exc | Native acc/good/exc | Same #1 | Good only in qBit | Good only in native | qBit s | Native s |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
        var examples = new List<string>();
        var totals = new Dictionary<string, (int QGood, int NGood, int QAcc, int NAcc, double QSec, double NSec, int Count)>();
        foreach (var target in targets)
        {
            var show = target.Show;
            var recipe = target.Recipe;
            var queries = target.Kind == MediaKind.TvSeasonPack
                ? planner.BuildSeasonPackQueries(recipe, show, [target.Season!.Value])
                : planner.BuildEpisodeQueries(recipe, show, target.Episode!);
            var recipePlugins = recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.SearchSource && module.IsEnabled)?.Plugins;
            var plugins = pluginsOverride ?? (string.IsNullOrWhiteSpace(recipePlugins) ? "enabled" : recipePlugins);
            var anime = RecipeRuntimeSettings.UsesAnimeAbsoluteEpisodeNumbering(recipe) ||
                        plugins.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(AnimeEngines.Contains);

            // Both backends run at the same time (independent networks paths); each has its own stopwatch.
            var qbitWatch = new Stopwatch();
            var qbitRows = new List<TorrentSearchResult>();
            async Task RunQbittorrentAsync()
            {
                qbitWatch.Start();
                foreach (var query in nativeOnly ? [] : queries)
                {
                    qbitRows.AddRange((await qbit.SearchAsync(query, plugins, maxSeconds, CancellationToken.None)).Rows);
                }

                qbitWatch.Stop();
            }

            var nativeWatch = new Stopwatch();
            var nativeRows = new List<TorrentSearchResult>();
            async Task RunNativeAsync()
            {
                nativeWatch.Start();
                var imdbId = await imdb.ResolveAsync(show.TmdbId);
                var qualities = RecipeCandidateFilter.GetFilterModule(recipe)?.QualityAllowList;
                var season = target.Kind == MediaKind.TvSeasonPack ? target.Season : target.Episode!.SeasonNumber;
                int? episodeNumber = target.Kind == MediaKind.TvSeasonPack ? null : target.Episode!.EpisodeNumber;
                // Same as NativeSearchService: one reduced query (+ quality forms for paged indexers) per distinct text.
                var nativeQueries = queries
                    .Select(query => (Reduced: NativeQueryText.Reduce(query, target.Kind), Specifics: NativeQueryText.SpecificQueries(query, target.Kind, qualities)))
                    .DistinctBy(pair => (pair.Reduced.ToLowerInvariant(), string.Join(",", pair.Specifics)))
                    .ToList();
                foreach (var (reduced, specifics) in nativeQueries)
                {
                    IReadOnlyList<IndexerQuery> batch =
                    [
                        new IndexerQuery(reduced, target.Kind, anime, imdbId, season, episodeNumber),
                        .. specifics.Select(specific => new IndexerQuery(specific, target.Kind, anime, imdbId, season, episodeNumber, SpecificOnly: true))
                    ];
                    var outcome = await orchestrator.SearchAsync(batch, indexers, SearchLane.Interactive, SearchBudget.Interactive, CancellationToken.None);
                    nativeRows.AddRange(outcome.Results);
                    Console.Error.WriteLine($"  [{show.Title} {target.Label}] native '{reduced}'{(imdbId is null ? "" : $" imdb={imdbId}")}: {outcome.Summary}");
                }

                nativeWatch.Stop();
            }

            await Task.WhenAll(RunQbittorrentAsync(), RunNativeAsync());

            var aliases = SearchIdentity.BuildAliases(nativeRows);
            string Key(TorrentSearchResult row) => SearchIdentity.Key(row, aliases);
            RecipeCandidateResult Evaluate(TorrentSearchResult row) => EvaluateRow(target, evaluator, row);
            if (dump is not null)
            {
                foreach (var (backend, rows) in new[] { ("native", nativeRows), ("qbittorrent", qbitRows) })
                {
                    foreach (var row in Unique(rows, Key))
                    {
                        var r = Evaluate(row);
                        var verdict = r.IsAccepted ? $"accepted:{CandidateTiers.Classify(recipe, r)}" : $"rejected:{r.RejectReason}";
                        dump.WriteLine($"{show.Title}\t{target.Label}\t{backend}\t{verdict}\t{(r.IsAccepted ? "" : r.RejectDetail)}\t{row.Seeders}\t{row.EngineName}\t{row.FileSize}\t{row.FileName.Replace('\t', ' ')}");
                    }
                }
            }

            var q = Tiers(recipe, qbitRows, Evaluate, autoTorrent, Key);
            var n = Tiers(recipe, nativeRows, Evaluate, autoTorrent, Key);
            var qTop = q.Accepted.OrderByDescending(r => r.TotalScore).FirstOrDefault();
            var nTop = n.Accepted.OrderByDescending(r => r.TotalScore).FirstOrDefault();
            var sameTop = qTop is not null && nTop is not null && Key(qTop.SearchResult) == Key(nTop.SearchResult);
            var qGood = q.Good.GroupBy(r => Key(r.SearchResult)).ToDictionary(g => g.Key, g => g.First());
            var nGood = n.Good.GroupBy(r => Key(r.SearchResult)).ToDictionary(g => g.Key, g => g.First());
            var onlyQ = qGood.Keys.Except(nGood.Keys).ToList();
            var onlyN = nGood.Keys.Except(qGood.Keys).ToList();
            Console.WriteLine(
                $"| {show.Title} | {target.Label} | {recipe.Name} | {q.Accepted.Count}/{q.Good.Count}/{q.Excellent} | {n.Accepted.Count}/{n.Good.Count}/{n.Excellent} | " +
                $"{(qTop is null || nTop is null ? "-" : sameTop ? "yes" : "no")} | {onlyQ.Count} | {onlyN.Count} | {qbitWatch.Elapsed.TotalSeconds:0} | {nativeWatch.Elapsed.TotalSeconds:0} |");

            var group = target.Kind == MediaKind.TvSeasonPack ? "pack" : anime ? "anime episode" : "tv episode";
            var t = totals.GetValueOrDefault(group);
            totals[group] = (t.QGood + q.Good.Count, t.NGood + n.Good.Count, t.QAcc + q.Accepted.Count, t.NAcc + n.Accepted.Count,
                t.QSec + qbitWatch.Elapsed.TotalSeconds, t.NSec + nativeWatch.Elapsed.TotalSeconds, t.Count + 1);

            foreach (var key in onlyQ.Take(3))
            {
                var nativeRow = nativeRows.FirstOrDefault(row => Key(row) == key);
                var why = nativeRow is null
                    ? "native: no such torrent"
                    : Evaluate(nativeRow) is { IsAccepted: true } accepted
                        ? $"native has it: accepted, tier {CandidateTiers.Classify(recipe, accepted)}, seeds {nativeRow.Seeders}, engine {nativeRow.EngineName}"
                        : $"native has it: rejected {Evaluate(nativeRow).RejectReason} {Evaluate(nativeRow).RejectDetail}";
                examples.Add($"- {show.Title} {target.Label}: only qBittorrent: {qGood[key].SearchResult.FileName} ({qGood[key].SearchResult.EngineName}, {qGood[key].SearchResult.Seeders} seeds) | {why}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("| Group | Targets | qBit good | Native good | Native/qBit | qBit accepted | Native accepted | qBit s | Native s |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var (group, t) in totals.OrderBy(pair => pair.Key))
        {
            var ratio = t.QGood == 0 ? "-" : $"{100.0 * t.NGood / t.QGood:0}%";
            Console.WriteLine($"| {group} | {t.Count} | {t.QGood} | {t.NGood} | {ratio} | {t.QAcc} | {t.NAcc} | {t.QSec:0} | {t.NSec:0} |");
        }

        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Good candidates only qBittorrent found (what native is missing):");
            examples.ForEach(Console.WriteLine);
        }

        return 0;
    }

    internal static RecipeCandidateResult EvaluateRow(Target target, CandidateEvaluationService evaluator, TorrentSearchResult row)
    {
        if (target.Kind != MediaKind.TvSeasonPack)
        {
            return evaluator.EvaluateEpisode(target.Recipe, target.Show, target.Episode!, row);
        }

        // Pack identity as FetchJobService.MapSeasonPackCandidatesAsync does it, without the metadata probe.
        var parsed = TorrentCandidateParser.Parse(row.FileName);
        var reject = SeasonPackIdentity.GetRejectReason(target.Show, [target.Season!.Value], row, parsed, target.Recipe);
        if (reject.Reason != CandidateRejectReason.None)
        {
            return new RecipeCandidateResult { SearchResult = row, RejectReason = reject.Reason, RejectDetail = reject.Detail };
        }

        var filter = RecipeCandidateFilter.GetFilterModule(target.Recipe);
        var quality = TorrentQuality.GetRank(parsed.Quality);
        return new RecipeCandidateResult
        {
            SearchResult = row,
            QualityScore = quality,
            PreferTermsScore = PreferredTermMatcher.CountMatches(row.FileName, filter?.PreferTerms),
            // Packs are ordered by quality, then seeders (MapSeasonPackCandidatesAsync).
            TotalScore = (quality * 100_000) + Math.Min(row.Seeders, 99_999)
        };
    }

    private static IEnumerable<TorrentSearchResult> Unique(IEnumerable<TorrentSearchResult> rows, Func<TorrentSearchResult, string> key) =>
        rows.Where(row => !string.IsNullOrWhiteSpace(row.FileUrl))
            .GroupBy(key)
            .Select(group => group.OrderByDescending(row => row.Seeders).First());

    private static (List<RecipeCandidateResult> Accepted, List<RecipeCandidateResult> Good, int Excellent) Tiers(
        SearchRecipe recipe,
        IEnumerable<TorrentSearchResult> rows,
        Func<TorrentSearchResult, RecipeCandidateResult> evaluate,
        AutoTorrentSettings autoTorrent,
        Func<TorrentSearchResult, string> key)
    {
        var accepted = Unique(rows, key).Select(evaluate).Where(r => r.IsAccepted).ToList();
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

    /// <summary>Latest aired episode(s) and latest fully aired season of every tracked show, with the show's recipes.</summary>
    private static List<Target> LoadTargets(string state, List<SearchRecipe> recipes, int episodesPerShow, bool packs)
    {
        var cutoff = DateTime.Today.AddDays(-2);
        var targets = new List<Target>();
        foreach (var (show, episodes) in LoadShows(state))
        {
            var aired = episodes.Where(ep => ep.AirDate is { } air && air <= cutoff).ToList();
            foreach (var episode in aired.OrderByDescending(ep => ep.AirDate).ThenByDescending(ep => ep.SeasonNumber).ThenByDescending(ep => ep.EpisodeNumber).Take(episodesPerShow))
            {
                targets.Add(EpisodeTarget(recipes, show, episode));
            }

            if (packs)
            {
                // Latest season whose every episode aired at least a week ago.
                var packSeason = episodes
                    .GroupBy(ep => ep.SeasonNumber)
                    .Where(g => g.All(ep => ep.AirDate is { } air && air <= DateTime.Today.AddDays(-7)))
                    .Select(g => (int?)g.Key)
                    .Max();
                if (packSeason is not null)
                {
                    targets.Add(PackTarget(recipes, show, packSeason.Value));
                }
            }
        }

        return targets;
    }

    internal static Target EpisodeTarget(List<SearchRecipe> recipes, TrackedShow show, TrackedEpisode episode) =>
        new(show, MediaKind.TvEpisode, Find(recipes, show.RecipeId, MediaKind.TvEpisode, "default-tv"), episode, null, $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}");

    internal static Target PackTarget(List<SearchRecipe> recipes, TrackedShow show, int season) =>
        new(show, MediaKind.TvSeasonPack, Find(recipes, show.PackRecipeId, MediaKind.TvSeasonPack, "default-tv-pack"), null, season, $"S{season:00} pack");

    /// <summary>Every tracked show with its episodes (season 0 excluded), from the app database opened read-only.</summary>
    internal static List<(TrackedShow Show, List<TrackedEpisode> Episodes)> LoadShows(string state)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(state, "media-manager.db")};Mode=ReadOnly");
        connection.Open();
        var shows = new List<TrackedShow>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "select Id, TmdbId, Title, FirstAirYear, AlternativeTitlesJson, ExcludedAlternativeTitlesJson, RecipeId, PackRecipeId, EpisodeGroupId from TrackedShows order by Title";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                shows.Add(new TrackedShow
                {
                    Id = reader.GetInt64(0),
                    TmdbId = reader.GetInt32(1),
                    Title = reader.GetString(2),
                    FirstAirYear = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    AlternativeTitlesJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ExcludedAlternativeTitlesJson = reader.IsDBNull(5) ? null : reader.GetString(5),
                    RecipeId = reader.IsDBNull(6) ? null : reader.GetString(6),
                    PackRecipeId = reader.IsDBNull(7) ? null : reader.GetString(7),
                    EpisodeGroupId = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }
        }

        var result = new List<(TrackedShow, List<TrackedEpisode>)>();
        foreach (var show in shows)
        {
            var episodes = new List<TrackedEpisode>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "select Id, SeasonNumber, EpisodeNumber, Title, AirDate from TrackedEpisodes where ShowId = $id and SeasonNumber > 0";
                command.Parameters.AddWithValue("$id", show.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    episodes.Add(new TrackedEpisode
                    {
                        Id = reader.GetInt64(0),
                        ShowId = show.Id,
                        SeasonNumber = reader.GetInt32(1),
                        EpisodeNumber = reader.GetInt32(2),
                        Title = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        AirDate = reader.IsDBNull(4) ? null : DateTime.TryParse(reader.GetString(4), out var air) ? air : null
                    });
                }
            }

            result.Add((show, episodes));
        }

        return result;
    }

    internal static SearchRecipe Find(List<SearchRecipe> recipes, string? id, MediaKind kind, string fallbackId) =>
        recipes.FirstOrDefault(r => r.RecipeId == id && r.TargetKind == kind)
        ?? recipes.FirstOrDefault(r => r.RecipeId == fallbackId)
        ?? recipes.First(r => r.TargetKind == kind);

    /// <summary>
    /// IMDb id of a TMDB show (EZTV searches by it), from the app's imdb-ids.json cache or TMDB external_ids with the
    /// app's read token. The token is only sent to TMDB and never printed.
    /// </summary>
    private sealed class ImdbResolver
    {
        private readonly Dictionary<int, string?> _cache = [];
        private readonly Dictionary<string, string> _appCache = [];
        private readonly string? _token;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10), BaseAddress = new Uri("https://api.themoviedb.org/3/") };

        public ImdbResolver(string state)
        {
            try
            {
                var appCache = Path.Combine(state, "imdb-ids.json");
                if (File.Exists(appCache))
                {
                    _appCache = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(appCache)) ?? [];
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(state, "settings.json")));
                if (doc.RootElement.TryGetProperty("TmdbReadAccessToken", out var token) && token.ValueKind == JsonValueKind.String)
                {
                    _token = token.GetString();
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
            }
        }

        public async Task<string?> ResolveAsync(int tmdbId)
        {
            if (_cache.TryGetValue(tmdbId, out var known))
            {
                return known;
            }

            string? id = _appCache.GetValueOrDefault($"tv:{tmdbId}");
            if (id is null && !string.IsNullOrWhiteSpace(_token))
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"tv/{tmdbId}/external_ids");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                    using var response = await _http.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        id = doc.RootElement.TryGetProperty("imdb_id", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    Console.Error.WriteLine($"  imdb lookup for tv:{tmdbId} failed: {ex.GetType().Name}");
                }
            }

            _cache[tmdbId] = string.IsNullOrWhiteSpace(id) ? null : id;
            return _cache[tmdbId];
        }
    }
}
