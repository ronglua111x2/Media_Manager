using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using Microsoft.Data.Sqlite;
using SearchBench;

// SearchBench movies [--state D:\MediaManagerState] [--only "Coherence,Liz"] [--max-seconds 60] [--plugins enabled]
//
// For every tracked movie: builds queries with the app's SearchPlanBuilder, searches them on
// qBittorrent (read-only), evaluates rows with the app's CandidateEvaluationService and recipe dedupe,
// and compares with an emulation of the pre-P1 rules (exact-year Contains, search titles only, no
// accent folding, no year-less query). Prints a Markdown table.

var argsMap = ParseArgs(args);
var command = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "movies";
var state = argsMap.GetValueOrDefault("state") ?? @"D:\MediaManagerState";
var only = (argsMap.GetValueOrDefault("only") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var maxSeconds = int.TryParse(argsMap.GetValueOrDefault("max-seconds"), out var ms) ? ms : 60;
var pluginsOverride = argsMap.GetValueOrDefault("plugins");
var useNative = string.Equals(argsMap.GetValueOrDefault("backend"), "native", StringComparison.OrdinalIgnoreCase);
using var nativeHttp = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
var governor = new media_management_app.Search.IndexerRequestGovernor();
var responseCache = new media_management_app.Search.IndexerResponseCache();
var orchestrator = new media_management_app.Search.SearchOrchestrator(governor, (id, lane, budget) =>
    new media_management_app.Search.GovernedIndexerHttp(nativeHttp, governor, responseCache, id, lane, budget.MaxGovernorWait, TimeSpan.FromMinutes(30), "MediaManager/1.0 (+native-search; SearchBench)"));
var nativeIndexers = media_management_app.Search.NativeIndexerCatalog.Create();

if (command == "compare")
{
    return await CompareCommand.RunAsync(argsMap);
}

if (command != "movies")
{
    Console.Error.WriteLine("Commands: movies, compare.");
    return 2;
}

var recipeJson = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter() }
};
var recipes = Directory.EnumerateFiles(Path.Combine(state, "Recipes"), "*.rcp")
    .Select(path => JsonSerializer.Deserialize<SearchRecipe>(File.ReadAllText(path), recipeJson)!)
    .ToList();
var autoTorrent = LoadAutoTorrent(state);
var movies = LoadMovies(state)
    .Where(movie => only.Length == 0 || only.Any(o => movie.Title.Contains(o, StringComparison.OrdinalIgnoreCase)))
    .ToList();

var planner = new SearchPlanBuilder(new SearchTitleResolver());
var evaluator = new CandidateEvaluationService(new SearchTitleResolver());
using var backend = new QbittorrentSearchBackend();
await backend.InitAsync(CancellationToken.None);
var cache = new Dictionary<string, SearchRun>(StringComparer.OrdinalIgnoreCase);

Console.WriteLine("| Movie | Year | Recipe | Queries old→new | Rows | Accepted old | Accepted new | Top new candidate |");
Console.WriteLine("|---|---|---|---|---|---|---|---|");
foreach (var movie in movies)
{
    var recipe = recipes.FirstOrDefault(r => r.RecipeId == movie.RecipeId && r.TargetKind == MediaKind.Movie)
                 ?? recipes.First(r => r.TargetKind == MediaKind.Movie && r.RecipeId.StartsWith("default", StringComparison.OrdinalIgnoreCase));
    var source = recipe.Modules.FirstOrDefault(m => m.BlockType == RecipeBlockType.SearchSource && m.IsEnabled);
    var plugins = pluginsOverride ?? (string.IsNullOrWhiteSpace(source?.Plugins) ? "enabled" : source!.Plugins);

    var newQueries = planner.BuildMovieQueries(recipe, movie);
    var oldQueries = OldMovieQueries(recipe, movie);

    var rowsNew = await RunAll(newQueries);
    var rowsOld = await RunAll(oldQueries);

    var acceptedNew = evaluator.EvaluateMovie;
    var newAccepted = rowsNew.Select(row => evaluator.EvaluateMovie(recipe, movie, row)).Where(r => r.IsAccepted).ToList();
    if (RecipeRuntimeSettings.GetDeduplicateCandidates(recipe, autoTorrent))
    {
        newAccepted = CandidateDeduplicator.Deduplicate(
            newAccepted.OrderByDescending(r => r.TotalScore),
            r => r.SearchResult.FileName, r => r.SearchResult.FileSize, r => r.SearchResult.FileUrl, r => r.SearchResult.Seeders,
            RecipeRuntimeSettings.GetFuzzyDeduplicate(recipe, autoTorrent),
            RecipeRuntimeSettings.GetFuzzyDeduplicateSizeToleranceMb(recipe, autoTorrent));
    }

    var oldAccepted = rowsOld.Count(row => OldMovieAccepts(recipe, movie, row));
    if (argsMap.ContainsKey("diag"))
    {
        var newRaw = rowsNew.Count(row => evaluator.EvaluateMovie(recipe, movie, row).IsAccepted);
        var lost = rowsOld.Where(row => OldMovieAccepts(recipe, movie, row))
            .Select(row => (Row: row, Eval: evaluator.EvaluateMovie(recipe, movie, row)))
            .Where(item => !item.Eval.IsAccepted)
            .ToList();
        Console.Error.WriteLine($"DIAG {movie.Title}: old={oldAccepted} newRaw={newRaw} newDeduped={newAccepted.Count} oldAcceptedButNewRejects={lost.Count}");
        foreach (var group in lost.GroupBy(item => item.Eval.RejectReason + ": " + item.Eval.RejectDetail).OrderByDescending(g => g.Count()).Take(6))
        {
            Console.Error.WriteLine($"   {group.Count(),3} x {group.Key}  e.g. {group.First().Row.FileName}");
        }
    }
    var top = newAccepted.OrderByDescending(r => r.TotalScore).FirstOrDefault();
    Console.WriteLine($"| {movie.Title} | {movie.ReleaseYear} | {recipe.Name} | {oldQueries.Count}→{newQueries.Count} | {rowsNew.Count} | {oldAccepted} | {newAccepted.Count} | {(top is null ? "-" : Escape(top.SearchResult.FileName) + $" ({top.SearchResult.Seeders} seeds, {top.SearchResult.EngineName})")} |");
}

return 0;

async Task<List<TorrentSearchResult>> RunAll(IReadOnlyList<string> queries)
{
    var merged = new Dictionary<string, TorrentSearchResult>(StringComparer.OrdinalIgnoreCase);
    foreach (var query in queries)
    {
        if (!cache.TryGetValue(query + "|" + pluginsOverride, out var run))
        {
            if (useNative)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var native = await orchestrator.SearchAsync(
                    [new media_management_app.Search.IndexerQuery(query, MediaKind.Movie, IsAnime: false)],
                    nativeIndexers,
                    media_management_app.Search.SearchLane.Interactive,
                    media_management_app.Search.SearchBudget.Interactive,
                    CancellationToken.None);
                run = new SearchRun(query, native.Results, "native", watch.Elapsed.TotalSeconds);
            }
            else
            {
                run = await backend.SearchAsync(query, pluginsOverride ?? "enabled", maxSeconds, CancellationToken.None);
            }

            cache[query + "|" + pluginsOverride] = run;
            Console.Error.WriteLine($"  searched '{query}': {run.Rows.Count} rows, {run.Status}, {run.Seconds:0}s");
        }

        foreach (var row in run.Rows.Where(row => !string.IsNullOrWhiteSpace(row.FileUrl)))
        {
            merged.TryAdd(row.FileUrl, row);
        }
    }

    return merged.Values.ToList();
}

static List<string> OldMovieQueries(SearchRecipe recipe, TrackedMovie movie)
{
    // Pre-P1: templates rendered with the TMDB year only, old sanitize (apostrophe -> space, accents kept),
    // library alternative titles only when the recipe explicitly enabled them.
    var identity = recipe.Modules.FirstOrDefault(m => m.BlockType == RecipeBlockType.Identity);
    var query = recipe.Modules.FirstOrDefault(m => m.BlockType == RecipeBlockType.QueryBuilder && m.IsEnabled);
    var titles = OldSearchTitles(recipe, movie);
    var templates = query?.QueryTemplates is { Count: > 0 } t ? t : ["{title} {year} {quality} {audio}", "{title} {quality}", "{title} {year}"];
    var qualities = query?.QualityAllowList is { Count: > 0 } q ? q : [string.Empty];
    var result = new List<string>();
    foreach (var template in templates)
    foreach (var title in template.Contains("{title}") ? titles : [string.Empty])
    foreach (var quality in template.Contains("{quality}") ? qualities : [string.Empty])
    {
        var rendered = template.Replace("{title}", title).Replace("{year}", movie.ReleaseYear?.ToString() ?? "")
            .Replace("{quality}", quality).Replace("{audio}", "");
        rendered = Regex.Replace(Regex.Replace(rendered, @"[^\p{L}\p{N}\s-]", " "), @"\s+", " ").Trim();
        if (rendered.Length > 0 && !result.Contains(rendered, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(rendered);
        }
    }

    return result;
}

static List<string> OldSearchTitles(SearchRecipe recipe, TrackedMovie movie)
{
    var identity = recipe.Modules.FirstOrDefault(m => m.BlockType == RecipeBlockType.Identity);
    var titles = new List<string> { movie.Title };
    if (identity?.IsEnabled == true)
    {
        titles.AddRange(identity.Aliases);
        if (identity.ExtensionData.TryGetValue(RecipeRuntimeSettings.UseLibraryEnglishTitlesKey, out var flag) &&
            bool.TryParse(flag, out var on) && on)
        {
            titles.AddRange(EnglishAlternativeTitleFilter.SelectLibraryAlternativeTitlesForSearch(
                movie.GetSearchableAlternativeTitles(), movie.Title,
                RecipeRuntimeSettings.GetMaxLibraryAlternativeTitlesForSearch(identity)));
        }
    }

    return titles.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

static bool OldMovieAccepts(SearchRecipe recipe, TrackedMovie movie, TorrentSearchResult result)
{
    // Mirror of CandidateEvaluationService.EvaluateMovie before native-search P1.
    var parsed = TorrentCandidateParser.Parse(result.FileName);
    if (RecipeCandidateFilter.GetRejectReason(recipe, result, parsed).Reason != CandidateRejectReason.None)
    {
        return false;
    }

    if (TorrentReleaseKind.GetRejectReasonForTarget(MediaKind.Movie, TorrentReleaseKind.Classify(result.FileName, parsed)) is not null)
    {
        return false;
    }

    if (movie.ReleaseYear is not null && !result.FileName.Contains(movie.ReleaseYear.Value.ToString(), StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var candidateTokens = parsed.TitleTokens.Count > 0 ? parsed.TitleTokens : TorrentCandidateParser.Tokenize(result.FileName).ToList();
    foreach (var title in OldSearchTitles(recipe, movie))
    {
        var tokens = TorrentCandidateParser.Tokenize(title).Where(t => t.Length > 2).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tokens.Count == 0)
        {
            return true;
        }

        var matched = tokens.Count(token => candidateTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
        if (matched >= Math.Max(1, Math.Min(2, tokens.Count)))
        {
            return true;
        }
    }

    return false;
}

static AutoTorrentSettings LoadAutoTorrent(string state)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(state, "settings.json")));
    return doc.RootElement.TryGetProperty("AutoTorrent", out var at)
        ? JsonSerializer.Deserialize<AutoTorrentSettings>(at.GetRawText()) ?? new AutoTorrentSettings()
        : new AutoTorrentSettings();
}

static List<TrackedMovie> LoadMovies(string state)
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

static string Escape(string value) => value.Replace("|", "/");

static Dictionary<string, string> ParseArgs(string[] args)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
        {
            map[args[i][2..]] = args[i + 1];
            i++;
        }
    }

    return map;
}
