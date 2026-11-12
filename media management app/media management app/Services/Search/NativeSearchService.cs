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
    // Lookups that found nothing, with the time to try again: TMDB being blocked for a night must not stop
    // EZTV (IMDb-only) for the rest of the session (audit 07c NS-20).
    private readonly Dictionary<string, DateTimeOffset> _imdbMisses = new(StringComparer.Ordinal);
    private static readonly TimeSpan ImdbLookupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ImdbMissRetry = TimeSpan.FromHours(1);
    private static readonly TimeSpan ImdbFailureRetry = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _imdbGate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, MemoEntry> _recent = new(StringComparer.OrdinalIgnoreCase);
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
        var specifics = NativeQueryText.SpecificQueries(query, kind, RecipeCandidateFilter.GetFilterModule(recipe)?.QualityAllowList);
        var settings = Settings;
        var lane = LaneScope.Value ?? SearchLane.Interactive;
        var enabled = string.Join(",", NativeIndexerCatalog.AllIds.Where(id => NativeIndexerCatalog.IsEnabled(settings, id)));
        // The lane and the enabled indexers are part of the key: a background answer may wait 5 minutes, and an
        // indexer turned on in Settings must be asked at once (audit 07c NS-06/07).
        var key = $"{kind}|{IsAnimeRecipe(recipe)}|{subject?.TmdbId}|{subject?.Season}|{subject?.Episode}|{reduced}|{string.Join(",", specifics)}|{lane}|{enabled}";
        while (true)
        {
            var now = DateTimeOffset.UtcNow;
            SweepRecent(now);
            var mine = new MemoEntry(now + RecentTtl, new Lazy<Task<NativeOutcome>>(() => SearchReducedAsync(recipe, reduced, specifics, kind, cancellationToken, subject)));
            var entry = _recent.GetOrAdd(key, mine);
            var owned = ReferenceEquals(entry, mine);
            if (!owned && entry.Expires <= now)
            {
                _recent.TryRemove(new KeyValuePair<string, MemoEntry>(key, entry));
                continue;
            }

            NativeOutcome outcome;
            try
            {
                // A caller that joins a search another caller started waits with its own token (Cancel button,
                // budget), not with the starter's.
                outcome = await entry.Search.Value.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!owned && !cancellationToken.IsCancellationRequested)
            {
                // The search we joined was cancelled by its starter: run our own.
                _recent.TryRemove(new KeyValuePair<string, MemoEntry>(key, entry));
                continue;
            }
            catch (OperationCanceledException) when (!owned)
            {
                throw;
            }
            catch
            {
                _recent.TryRemove(new KeyValuePair<string, MemoEntry>(key, entry));
                throw;
            }

            if (owned && (outcome.Rows.Count == 0 || !outcome.Complete))
            {
                // Do not remember empty or degraded answers (a site timed out, was blocked or refused by the
                // governor): the next search asks those sites again. Healthy sites answer from the HTTP cache.
                _recent.TryRemove(new KeyValuePair<string, MemoEntry>(key, entry));
            }

            return outcome.Rows;
        }
    }

    private sealed record MemoEntry(DateTimeOffset Expires, Lazy<Task<NativeOutcome>> Search);

    private readonly record struct NativeOutcome(IReadOnlyList<TorrentSearchResult> Rows, bool Complete);

    /// <summary>Drops expired memo entries: keys rarely repeat (new episodes), so they would pile up (NS-08).</summary>
    private void SweepRecent(DateTimeOffset now)
    {
        foreach (var pair in _recent)
        {
            if (pair.Value.Expires <= now && pair.Value.Search.IsValueCreated && pair.Value.Search.Value.IsCompleted)
            {
                _recent.TryRemove(pair);
            }
        }
    }

    /// <summary>
    /// True when every indexer that was asked answered ("ok"), or was skipped only because it is disabled.
    /// A timeout, block, rate limit, breaker or parse error makes the answer partial.
    /// </summary>
    public static bool IsComplete(IReadOnlyList<IndexerRunTelemetry> runs) =>
        runs.All(run => run.Status == "ok" || (run.Status == "skipped" && run.Detail == "disabled"));

    private async Task<NativeOutcome> SearchReducedAsync(SearchRecipe recipe, string query, IReadOnlyList<string> specifics, MediaKind kind, CancellationToken cancellationToken, SearchSubject? subject)
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
            // Paged HTML indexers also get quality-bearing forms (plan 08: they reach deeper encodes).
            [
                new IndexerQuery(query, kind, isAnime, imdbId, subject?.Season, subject?.Episode),
                .. specifics.Select(specific => new IndexerQuery(specific, kind, isAnime, imdbId, subject?.Season, subject?.Episode, SpecificOnly: true))
            ],
            NativeIndexerCatalog.Create(settings),
            lane,
            budget,
            cancellationToken);
        _logger.Info($"[Search] native query='{query}' kind={kind}{(isAnime ? " anime" : "")} lane={lane}: {outcome.Summary}", LogTarget.File | LogTarget.Console);
        return new NativeOutcome(outcome.Results, IsComplete(outcome.Indexers));
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
            // TPB description pages (qBittorrent plugin) and apibay hashes (native) are the same torrents.
            var aliases = SearchIdentity.BuildAliases(nativeRows);
            var native = Summarize(recipe, subject, nativeRows, aliases);
            var qbit = Summarize(recipe, subject, qbitRows, aliases);
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
        IReadOnlyList<TorrentSearchResult> rows,
        IReadOnlyDictionary<string, string> aliases)
    {
        var keys = rows.Select(row => SearchIdentity.Key(row, aliases)).ToHashSet(StringComparer.Ordinal);
        if (subject?.Evaluate is null)
        {
            return (keys, new HashSet<string>(StringComparer.Ordinal), 0, null, null);
        }

        var evaluated = rows.Select(row => subject.Evaluate(row)).Where(result => result.IsAccepted).ToList();
        var tiers = evaluated.Select(result => (Result: result, Tier: CandidateTiers.Classify(recipe, result))).ToList();
        var good = tiers.Where(item => item.Tier >= CandidateTier.Good)
            .Select(item => SearchIdentity.Key(item.Result.SearchResult, aliases))
            .ToHashSet(StringComparer.Ordinal);
        var excellent = tiers.Where(item => item.Tier == CandidateTier.Excellent)
            .Select(item => SearchIdentity.Key(item.Result.SearchResult, aliases))
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

            if (_imdbMisses.TryGetValue(key, out var retryAt) && retryAt > DateTimeOffset.UtcNow)
            {
                return null;
            }

            string? imdbId = null;
            var failed = false;
            // The IMDb id only helps (EZTV, YTS exact lookup): a slow or blocked TMDB must neither hold the search
            // for its 100 s HttpClient timeout nor look like the user pressing Cancel (audit 07c NS-03).
            using var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lookup.CancelAfter(ImdbLookupTimeout);
            try
            {
                imdbId = await _externalIds.GetImdbIdAsync(subject.IsMovie, subject.TmdbId, lookup.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failed = true;
                _logger.Warning($"[Search] IMDb id lookup for {key} timed out; searching without it.", LogTarget.File);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed = true;
                _logger.Warning($"[Search] could not resolve the IMDb id for {key}: {ex.Message}", LogTarget.File);
            }

            if (string.IsNullOrWhiteSpace(imdbId))
            {
                _imdbMisses[key] = DateTimeOffset.UtcNow + (failed ? ImdbFailureRetry : ImdbMissRetry);
                return null;
            }

            _imdbMisses.Remove(key);

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
