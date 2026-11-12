using System.Net;
using System.Net.Http;
using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Search;

namespace media_management_app.Services;

public interface INativeSearchService
{
    /// <summary>True when searches for this media kind go to the native indexers (Search.Backend = Native or Both).</summary>
    bool UsesNative(MediaKind kind, SearchRecipe? recipe = null);

    /// <summary>True when the qBittorrent search should also run for comparison (Search.Backend = Both).</summary>
    bool ComparesWithQbittorrent(MediaKind kind, SearchRecipe? recipe = null);

    /// <summary>Runs one query on the native indexers (governed, cached) and returns rows in the app's result shape.</summary>
    Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(SearchRecipe recipe, string query, MediaKind kind, CancellationToken cancellationToken, SearchSubject? subject = null);

    /// <summary>
    /// Native search for a query; in "Both" mode also runs <paramref name="qbittorrentSearch"/> and logs a
    /// comparison. Returns native rows when native is selected, otherwise the qBittorrent rows.
    /// </summary>
    Task<IReadOnlyList<TorrentSearchResult>> SearchOrFallbackAsync(
        SearchRecipe recipe,
        string query,
        MediaKind kind,
        Func<Task<IReadOnlyList<TorrentSearchResult>>> qbittorrentSearch,
        CancellationToken cancellationToken,
        SearchSubject? subject = null);

    IReadOnlyList<IndexerStateSnapshot> HealthSnapshot();

    bool PauseAll { get; set; }
}

/// <summary>What is being searched, so id-based indexers (EZTV by IMDb id) can be used and filtered.</summary>
public sealed record SearchSubject(
    bool IsMovie,
    int TmdbId,
    int? Season = null,
    int? Episode = null,
    Func<TorrentSearchResult, RecipeCandidateResult>? Evaluate = null);

/// <summary>
/// App side of native search (native-search P2): builds the indexer set from settings, owns the request
/// governor (state persisted to &lt;StateFolder&gt;/indexer-state.json so limits survive restarts) and the
/// response cache, and routes each query according to the Search.Backend flag. Results are converted to
/// <see cref="TorrentSearchResult"/> so evaluation, dedupe, cart and the add gate stay unchanged.
/// </summary>
public sealed class NativeSearchService : INativeSearchService, IDisposable
{
    private const string StateFileName = "indexer-state.json";
    private const string ImdbCacheFileName = "imdb-ids.json";
    private const string CompareLogFileName = "search-compare.jsonl";
    private static readonly object CompareLogGate = new();
    private const string UserAgent = "MediaManager/1.0 (+native-search)";
    private static readonly JsonSerializerOptions StateJson = new() { WriteIndented = true };
    private static readonly AsyncLocal<SearchLane?> LaneScope = new();

    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly HttpClient _http;
    private readonly IndexerResponseCache _cache = new();
    private readonly IndexerRequestGovernor _governor;
    private readonly ITmdbExternalIdService _externalIds;
    private readonly Dictionary<string, string> _imdbIds;
    private readonly HashSet<string> _imdbMisses = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _imdbGate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset Expires, Task<IReadOnlyList<TorrentSearchResult>> Rows)> _recent =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan RecentTtl = TimeSpan.FromMinutes(15);
    private readonly object _saveGate = new();
    private System.Threading.Timer? _saveTimer;

    public NativeSearchService(ISettingsService settingsService, IAppLogger logger, ITmdbExternalIdService externalIds)
    {
        _settingsService = settingsService;
        _logger = logger;
        _externalIds = externalIds;
        _imdbIds = LoadImdbCache();
        _http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _governor = new IndexerRequestGovernor(
            TimeProvider.System,
            id => NativeIndexerCatalog.PolicyResolver(Settings)(id),
            LoadState());
        _governor.KillSwitch = Settings.PauseAll;
        _governor.StateChanged += ScheduleSave;
    }

    /// <summary>Marks searches in the current async flow as background work (Auto-Track): they get a smaller budget share.</summary>
    public static IDisposable BackgroundScope()
    {
        var previous = LaneScope.Value;
        LaneScope.Value = SearchLane.Background;
        return new Restore(() => LaneScope.Value = previous);
    }

    public bool PauseAll
    {
        get => _governor.KillSwitch;
        set
        {
            _governor.KillSwitch = value;
            Settings.PauseAll = value;
        }
    }

    private NativeSearchSettings Settings => _settingsService.Current.Search ??= new NativeSearchSettings();

    public bool UsesNative(MediaKind kind, SearchRecipe? recipe = null) => BackendFor(kind, recipe) is SearchBackends.Native or SearchBackends.Both;

    public bool ComparesWithQbittorrent(MediaKind kind, SearchRecipe? recipe = null) => BackendFor(kind, recipe) == SearchBackends.Both;

    public IReadOnlyList<IndexerStateSnapshot> HealthSnapshot() => _governor.Snapshot();

    public async Task<IReadOnlyList<TorrentSearchResult>> SearchOrFallbackAsync(
        SearchRecipe recipe,
        string query,
        MediaKind kind,
        Func<Task<IReadOnlyList<TorrentSearchResult>>> qbittorrentSearch,
        CancellationToken cancellationToken,
        SearchSubject? subject = null)
    {
        if (!UsesNative(kind, recipe))
        {
            return await qbittorrentSearch();
        }

        if (!ComparesWithQbittorrent(kind, recipe))
        {
            return await SearchAsync(recipe, query, kind, cancellationToken, subject);
        }

        var nativeWatch = System.Diagnostics.Stopwatch.StartNew();
        var nativeTask = TimedAsync(() => SearchAsync(recipe, query, kind, cancellationToken, subject), nativeWatch);
        var qbitWatch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<TorrentSearchResult> qbitRows;
        try
        {
            qbitRows = await qbittorrentSearch();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            qbitRows = [];
            _logger.Warning($"[Search] compare: qBittorrent search failed for '{query}': {ex.Message}", LogTarget.File);
        }

        qbitWatch.Stop();
        var nativeRows = await nativeTask;
        WriteCompareRecord(recipe, query, kind, subject, nativeRows, nativeWatch.ElapsedMilliseconds, qbitRows, qbitWatch.ElapsedMilliseconds);
        return nativeRows;
    }

    public async Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(SearchRecipe recipe, string query, MediaKind kind, CancellationToken cancellationToken, SearchSubject? subject = null)
    {
        // Recipe templates multiply queries that full-text indexers answer identically ("X 2014 1080p",
        // "X 1080p", "X"): search the reduced text once and reuse it for a while (native-search P4).
        var reduced = NativeQueryText.Reduce(query, kind);
        var key = $"{kind}|{IsAnimeRecipe(recipe)}|{subject?.TmdbId}|{subject?.Season}|{subject?.Episode}|{reduced}";
        var now = DateTimeOffset.UtcNow;
        if (_recent.TryGetValue(key, out var recent) && recent.Expires > now)
        {
            try
            {
                return await recent.Rows;
            }
            catch
            {
                _recent.TryRemove(key, out _);
            }
        }

        var task = SearchReducedAsync(recipe, reduced, kind, cancellationToken, subject);
        _recent[key] = (now + RecentTtl, task);
        try
        {
            var rows = await task;
            if (rows.Count == 0)
            {
                // Do not remember empty answers: they may come from a governor refusal or a timeout.
                _recent.TryRemove(key, out _);
            }

            return rows;
        }
        catch
        {
            _recent.TryRemove(key, out _);
            throw;
        }
    }

    private async Task<IReadOnlyList<TorrentSearchResult>> SearchReducedAsync(SearchRecipe recipe, string query, MediaKind kind, CancellationToken cancellationToken, SearchSubject? subject)
    {
        var settings = Settings;
        var lane = LaneScope.Value ?? SearchLane.Interactive;
        var overall = lane == SearchLane.Background
            ? TimeSpan.FromMinutes(5)
            : TimeSpan.FromSeconds(Math.Clamp(settings.OverallTimeoutSeconds, 10, 600));
        var budget = new SearchBudget(TimeSpan.FromSeconds(Math.Clamp(settings.PerIndexerTimeoutSeconds, 3, 60)), overall, overall);
        foreach (var id in NativeIndexerCatalog.AllIds)
        {
            _governor.SetDisabled(id, !NativeIndexerCatalog.IsEnabled(settings, id));
        }

        var orchestrator = new SearchOrchestrator(_governor, (id, searchLane, searchBudget) => new GovernedIndexerHttp(
            _http, _governor, _cache, id, searchLane, searchBudget.MaxGovernorWait, TimeSpan.FromMinutes(45), UserAgent, searchBudget.PerIndexer));
        var isAnime = IsAnimeRecipe(recipe);
        // TV: EZTV searches by IMDb id. Movies: YTS accepts the IMDb id for an exact lookup (plan 08 §Q2).
        var imdbId = subject is not null ? await ResolveImdbIdAsync(subject, cancellationToken) : null;
        var outcome = await orchestrator.SearchAsync(
            [new IndexerQuery(query, kind, isAnime, imdbId, subject?.Season, subject?.Episode)],
            NativeIndexerCatalog.Create(settings),
            lane,
            budget,
            cancellationToken);
        _logger.Info($"[Search] native query='{query}' kind={kind}{(isAnime ? " anime" : "")} lane={lane}: {outcome.Summary}", LogTarget.File | LogTarget.Console);
        return outcome.Results;
    }

    private static async Task<IReadOnlyList<TorrentSearchResult>> TimedAsync(
        Func<Task<IReadOnlyList<TorrentSearchResult>>> search,
        System.Diagnostics.Stopwatch watch)
    {
        try
        {
            return await search();
        }
        finally
        {
            watch.Stop();
        }
    }

    /// <summary>
    /// One JSON line per "Both" search in &lt;StateFolder&gt;/logs/search-compare.jsonl (native-search plan 08 §Q5):
    /// unique rows and Good/Excellent candidates of each backend, overlap, top candidate and time. SearchBench
    /// `gate` turns these real searches into the P6 cutover numbers.
    /// </summary>
    private void WriteCompareRecord(
        SearchRecipe recipe,
        string query,
        MediaKind kind,
        SearchSubject? subject,
        IReadOnlyList<TorrentSearchResult> nativeRows,
        long nativeMs,
        IReadOnlyList<TorrentSearchResult> qbitRows,
        long qbitMs)
    {
        try
        {
            var native = Summarize(recipe, subject, nativeRows);
            var qbit = Summarize(recipe, subject, qbitRows);
            var record = new
            {
                utc = DateTimeOffset.UtcNow,
                kind = kind.ToString(),
                recipe = recipe.Name,
                query,
                tmdbId = subject?.TmdbId,
                season = subject?.Season,
                episode = subject?.Episode,
                evaluated = subject?.Evaluate is not null,
                native = new { rows = native.Keys.Count, good = native.Good.Count, excellent = native.Excellent, top = native.TopName, topScore = native.TopScore, ms = nativeMs },
                qbittorrent = new { rows = qbit.Keys.Count, good = qbit.Good.Count, excellent = qbit.Excellent, top = qbit.TopName, topScore = qbit.TopScore, ms = qbitMs },
                both = native.Keys.Intersect(qbit.Keys).Count(),
                goodBoth = native.Good.Intersect(qbit.Good).Count(),
                goodNativeOnly = native.Good.Except(qbit.Good).Count(),
                goodQbittorrentOnly = qbit.Good.Except(native.Good).Count()
            };
            var line = JsonSerializer.Serialize(record);
            var folder = Path.Combine(_settingsService.Current.StateFolder, AppConstants.LogFolderName);
            Directory.CreateDirectory(folder);
            lock (CompareLogGate)
            {
                File.AppendAllText(Path.Combine(folder, CompareLogFileName), line + Environment.NewLine);
            }

            _logger.Info(
                $"[Search] compare query='{query}' kind={kind}: native rows={native.Keys.Count} good={native.Good.Count} ({nativeMs} ms), " +
                $"qbittorrent rows={qbit.Keys.Count} good={qbit.Good.Count} ({qbitMs} ms), good in both={record.goodBoth}.",
                LogTarget.File | LogTarget.Console);
        }
        catch (Exception ex)
        {
            _logger.Warning($"[Search] could not write {CompareLogFileName}: {ex.Message}", LogTarget.File);
        }
    }

    private static (HashSet<string> Keys, HashSet<string> Good, int Excellent, string? TopName, int? TopScore) Summarize(
        SearchRecipe recipe,
        SearchSubject? subject,
        IReadOnlyList<TorrentSearchResult> rows)
    {
        var keys = rows.Select(row => CandidateDeduplicator.IdentityKey(row.FileUrl)).ToHashSet(StringComparer.Ordinal);
        if (subject?.Evaluate is null)
        {
            return (keys, new HashSet<string>(StringComparer.Ordinal), 0, null, null);
        }

        var evaluated = rows.Select(row => subject.Evaluate(row)).Where(result => result.IsAccepted).ToList();
        var tiers = evaluated.Select(result => (Result: result, Tier: CandidateTiers.Classify(recipe, result))).ToList();
        var good = tiers.Where(item => item.Tier >= CandidateTier.Good)
            .Select(item => CandidateDeduplicator.IdentityKey(item.Result.SearchResult.FileUrl))
            .ToHashSet(StringComparer.Ordinal);
        var excellent = tiers.Where(item => item.Tier == CandidateTier.Excellent)
            .Select(item => CandidateDeduplicator.IdentityKey(item.Result.SearchResult.FileUrl))
            .Distinct()
            .Count();
        var top = evaluated.OrderByDescending(result => result.TotalScore).FirstOrDefault();
        return (keys, good, excellent, top?.SearchResult.FileName, top?.TotalScore);
    }

    /// <summary>Anime recipes also query anime-only indexers (nyaa). Detected from numbering mode or anime plugins.</summary>
    public static bool IsAnimeRecipe(SearchRecipe recipe)
    {
        if (RecipeRuntimeSettings.UsesAnimeAbsoluteEpisodeNumbering(recipe))
        {
            return true;
        }

        var plugins = recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.SearchSource)?.Plugins ?? string.Empty;
        return plugins.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(plugin => plugin is "nyaasi" or "subsplease" or "nekobt" or "tsukihime" or "animetosho");
    }

    private async Task<string?> ResolveImdbIdAsync(SearchSubject subject, CancellationToken cancellationToken)
    {
        var key = $"{(subject.IsMovie ? "movie" : "tv")}:{subject.TmdbId}";
        await _imdbGate.WaitAsync(cancellationToken);
        try
        {
            if (_imdbIds.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (_imdbMisses.Contains(key))
            {
                return null;
            }

            string? imdbId = null;
            try
            {
                imdbId = await _externalIds.GetImdbIdAsync(subject.IsMovie, subject.TmdbId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warning($"[Search] could not resolve the IMDb id for {key}: {ex.Message}", LogTarget.File);
            }

            if (string.IsNullOrWhiteSpace(imdbId))
            {
                _imdbMisses.Add(key);
                return null;
            }

            _imdbIds[key] = imdbId;
            SaveImdbCache();
            return imdbId;
        }
        finally
        {
            _imdbGate.Release();
        }
    }

    private string ImdbCachePath => Path.Combine(_settingsService.Current.StateFolder, ImdbCacheFileName);

    private Dictionary<string, string> LoadImdbCache()
    {
        try
        {
            return File.Exists(ImdbCachePath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ImdbCachePath)) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveImdbCache()
    {
        try
        {
            File.WriteAllText(ImdbCachePath, JsonSerializer.Serialize(_imdbIds, StateJson));
        }
        catch (Exception ex)
        {
            _logger.Warning($"[Search] could not save {ImdbCacheFileName}: {ex.Message}", LogTarget.File);
        }
    }

    /// <summary>Recipe choice first, then the per-kind override, then the Settings default.</summary>
    private string BackendFor(MediaKind kind, SearchRecipe? recipe = null)
    {
        if (recipe is not null && RecipeRuntimeSettings.GetSearchBackend(recipe) is { } fromRecipe)
        {
            return Normalize(fromRecipe);
        }

        var settings = Settings;
        if (settings.BackendByKind.TryGetValue(kind.ToString(), out var perKind) && !string.IsNullOrWhiteSpace(perKind))
        {
            return Normalize(perKind);
        }

        return Normalize(settings.Backend);

        static string Normalize(string? value) =>
            string.Equals(value, SearchBackends.Native, StringComparison.OrdinalIgnoreCase) ? SearchBackends.Native
            : string.Equals(value, SearchBackends.Both, StringComparison.OrdinalIgnoreCase) ? SearchBackends.Both
            : SearchBackends.Qbittorrent;
    }

    private string StatePath => Path.Combine(_settingsService.Current.StateFolder, StateFileName);

    private IReadOnlyList<IndexerStateSnapshot> LoadState()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<List<IndexerStateSnapshot>>(File.ReadAllText(StatePath)) ?? []
                : [];
        }
        catch (Exception ex)
        {
            _logger.Warning($"[Search] could not read {StateFileName}, starting with fresh indexer limits: {ex.Message}", LogTarget.File);
            return [];
        }
    }

    private void ScheduleSave()
    {
        lock (_saveGate)
        {
            _saveTimer ??= new System.Threading.Timer(_ => SaveState(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        }
    }

    private void SaveState()
    {
        try
        {
            var json = JsonSerializer.Serialize(_governor.Snapshot(), StateJson);
            var temp = StatePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, StatePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.Warning($"[Search] could not save {StateFileName}: {ex.Message}", LogTarget.File);
        }
    }

    public void Dispose()
    {
        _saveTimer?.Dispose();
        SaveState();
        _http.Dispose();
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
