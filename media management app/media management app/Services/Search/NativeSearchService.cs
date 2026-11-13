using System.Net;
using System.Net.Http;
using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Search;

namespace media_management_app.Services;

public interface INativeSearchService
{
    /// <summary>
    /// True when this recipe searches the native indexers: its backend is Native or Both, and native search is enabled
    /// and not paused. No recipe: qBittorrent.
    /// </summary>
    bool UsesNative(MediaKind kind, SearchRecipe? recipe = null);

    /// <summary>True when the qBittorrent search also runs for comparison (recipe backend Both, native available).</summary>
    bool ComparesWithQbittorrent(MediaKind kind, SearchRecipe? recipe = null);

    /// <summary>Settings → Search engines → Enable native search (the master switch; recipes choose the backend).</summary>
    bool NativeEnabled => true;

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

    /// <summary>The rate limits and daily budget that apply to an indexer (defaults, tightened by Settings).</summary>
    IndexerPolicy PolicyFor(string indexerId);

    /// <summary>"Native", "qBittorrent" or "Both" for logs (the backend this kind and recipe search with).</summary>
    string BackendName(MediaKind kind, SearchRecipe? recipe = null) =>
        ComparesWithQbittorrent(kind, recipe) ? "Both" : UsesNative(kind, recipe) ? "Native" : "qBittorrent";

    bool IsIndexerEnabled(string indexerId);

    /// <summary>Raised (on any thread) when a site's usage, breaker or cooldown changes, or the kill switch is toggled.</summary>
    event Action? HealthChanged;

    bool PauseAll { get; set; }
}

/// <summary>What is being searched, so id-based indexers (EZTV by IMDb id) can be used and filtered.</summary>
public sealed record SearchSubject(
    bool IsMovie,
    int TmdbId,
    int? Season = null,
    int? Episode = null,
    Func<TorrentSearchResult, RecipeCandidateResult>? Evaluate = null,
    int? Year = null);

/// <summary>
/// App side of native search (native-search P2): builds the indexer set from settings, owns the request
/// governor (state persisted to &lt;StateFolder&gt;/indexer-state.json so limits survive restarts) and the
/// response cache, and routes each query to the backend its recipe chose (<see cref="SearchBackendResolver"/>):
/// a Native/Both recipe searches with qBittorrent, with a warning, while native search is off or paused. Results are converted to
/// <see cref="TorrentSearchResult"/> so evaluation, dedupe, cart and the add gate stay unchanged.
/// </summary>
public sealed class NativeSearchService : INativeSearchService, IDisposable
{
    private const string StateFileName = IndexerStateFile.FileName;
    private const string ImdbCacheFileName = "imdb-ids.json";
    private const string CompareLogFileName = "search-compare.jsonl";
    private static readonly object CompareLogGate = new();
    private const string UserAgent = "MediaManager/1.0 (+native-search)";
    private static readonly JsonSerializerOptions StateJson = new() { WriteIndented = true };
    private static readonly AsyncLocal<SearchLane?> LaneScope = new();
    private static readonly AsyncLocal<SearchSession?> SessionScope = new();

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
    // Fallback warnings already logged (recipe + reason → time): a hunt asks once per episode, the log says it once.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _fallbackLogged = new(StringComparer.Ordinal);
    private static readonly TimeSpan FallbackLogInterval = TimeSpan.FromMinutes(30);
    private readonly object _saveGate = new();
    private readonly object _saveFileGate = new();
    private System.Threading.Timer? _saveTimer;
    // Exclusive lock on the state folder while the app runs: SearchBench refuses to run beside it (07d GOV-07).
    private readonly IDisposable? _stateLock;

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
            // Each request gets its own timeout (Settings: 3-60 s) in GovernedIndexerHttp (audit 07c NS-25).
            Timeout = Timeout.InfiniteTimeSpan
        };
        _stateLock = IndexerStateFile.TryLock(_settingsService.Current.StateFolder);
        if (_stateLock is null)
        {
            _logger.Warning(
                $"[Search] another process (SearchBench, or a second app) holds {IndexerStateFile.LockFileName}; its searches use the same sites' budget.",
                LogTarget.File);
        }

        var loaded = IndexerStateFile.Load(StatePath);
        if (loaded.Warning is not null)
        {
            _logger.Warning($"[Search] {loaded.Warning}", LogTarget.All);
        }

        _governor = new IndexerRequestGovernor(
            TimeProvider.System,
            id => NativeIndexerCatalog.PolicyResolver(Settings)(id),
            loaded.States);
        if (loaded.Cautious)
        {
            _governor.StartCautiously(NativeIndexerCatalog.AllIds);
        }

        _governor.KillSwitch = Settings.PauseAll;
        _governor.StateChanged += ScheduleSave;
        _governor.StateChanged += () => HealthChanged?.Invoke();
    }

    /// <summary>Marks searches in the current async flow as background work (Auto-Track): they get a smaller budget share.</summary>
    public static IDisposable BackgroundScope()
    {
        var previous = LaneScope.Value;
        LaneScope.Value = SearchLane.Background;
        return new Restore(() => LaneScope.Value = previous);
    }

    /// <summary>
    /// Starts a search session for one target (an episode, a season pack, a movie, a show snapshot) in the current
    /// async flow (audit 07d GOV-03 B): its queries share the per-search request cap, use the backend chosen when the
    /// session started, and record whether any search source could be asked. Searches outside a session get one each.
    /// </summary>
    public static SessionHandle BeginSession()
    {
        var previous = SessionScope.Value;
        var session = new SearchSession();
        SessionScope.Value = session;
        return new SessionHandle(session, () => SessionScope.Value = previous);
    }

    /// <summary>The search session of the current async flow, if one was started.</summary>
    public static SearchSession? CurrentSession => SessionScope.Value;

    public sealed class SessionHandle(SearchSession session, Action restore) : IDisposable
    {
        public SearchSession Session { get; } = session;

        public void Dispose() => restore();
    }

    public bool PauseAll
    {
        get => _governor.KillSwitch;
        set
        {
            _governor.KillSwitch = value;
            Settings.PauseAll = value;
            HealthChanged?.Invoke();
        }
    }

    private NativeSearchSettings Settings => _settingsService.Current.Search ??= new NativeSearchSettings();

    public bool NativeEnabled => Settings.NativeEnabled;

    public bool UsesNative(MediaKind kind, SearchRecipe? recipe = null) => BackendFor(kind, recipe) is SearchBackends.Native or SearchBackends.Both;

    public bool ComparesWithQbittorrent(MediaKind kind, SearchRecipe? recipe = null) => BackendFor(kind, recipe) == SearchBackends.Both;

    public IReadOnlyList<IndexerStateSnapshot> HealthSnapshot() => _governor.Snapshot();

    public IndexerPolicy PolicyFor(string indexerId) => NativeIndexerCatalog.PolicyResolver(Settings)(indexerId);

    public bool IsIndexerEnabled(string indexerId) => NativeIndexerCatalog.IsEnabled(Settings, indexerId);

    public event Action? HealthChanged;

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

        var session = SessionScope.Value ?? new SearchSession();
        var nativeWatch = System.Diagnostics.Stopwatch.StartNew();
        var nativeTask = TimedAsync(() => SearchWithMemoAsync(recipe, query, kind, cancellationToken, subject, session), nativeWatch);
        var qbitWatch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<TorrentSearchResult> qbitRows;
        string? qbitError = null;
        try
        {
            qbitRows = await qbittorrentSearch();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            qbitRows = [];
            qbitError = ex.Message;
            _logger.Warning($"[Search] compare: qBittorrent search failed for '{query}': {ex.Message}", LogTarget.File);
        }

        qbitWatch.Stop();
        var (nativeRows, fromMemo, nativeIndexers) = await nativeTask;
        WriteCompareRecord(recipe, query, kind, subject, nativeRows, nativeWatch.ElapsedMilliseconds, qbitRows, qbitWatch.ElapsedMilliseconds, qbitError, fromMemo);
        if (nativeRows.Count == 0 && qbitError is null && !nativeIndexers.Any(SearchSession.SourceAnswered))
        {
            // Both mode: no native site could be asked (paused, breaker, budget). The qBittorrent search ran anyway,
            // so use it rather than answer "no candidates".
            _logger.Warning($"[Search] Both: no native source answered for '{query}'; using the qBittorrent rows ({qbitRows.Count}).", LogTarget.File | LogTarget.Console);
            return qbitRows;
        }

        return nativeRows;
    }

    public async Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(SearchRecipe recipe, string query, MediaKind kind, CancellationToken cancellationToken, SearchSubject? subject = null) =>
        (await SearchWithMemoAsync(recipe, query, kind, cancellationToken, subject, SessionScope.Value ?? new SearchSession())).Rows;

    /// <summary>
    /// The native answer, whether it came from the 15-minute memo (another caller's search), and the per-site outcome.
    /// The outcome is recorded in <paramref name="session"/> either way.
    /// </summary>
    private async Task<(IReadOnlyList<TorrentSearchResult> Rows, bool FromMemo, IReadOnlyList<IndexerRunTelemetry> Indexers)> SearchWithMemoAsync(
        SearchRecipe recipe,
        string query,
        MediaKind kind,
        CancellationToken cancellationToken,
        SearchSubject? subject,
        SearchSession session)
    {
        // Recipe templates multiply queries that full-text indexers answer identically ("X 2014 1080p",
        // "X 1080p", "X"): search the reduced text once and reuse it for a while (native-search P4).
        var movieYear = subject is { IsMovie: true } ? subject.Year : null;
        var reduced = NativeQueryText.Reduce(query, kind, movieYear);
        var specifics = NativeQueryText.SpecificQueries(query, kind, RecipeCandidateFilter.GetFilterModule(recipe)?.QualityAllowList, movieYear);
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
            var mine = new MemoEntry(now + RecentTtl, new Lazy<Task<NativeOutcome>>(() => SearchReducedAsync(recipe, reduced, specifics, kind, cancellationToken, subject, session)));
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

            session.Record(outcome.Indexers);
            return (outcome.Rows, !owned, outcome.Indexers);
        }
    }

    private sealed record MemoEntry(DateTimeOffset Expires, Lazy<Task<NativeOutcome>> Search);

    private readonly record struct NativeOutcome(IReadOnlyList<TorrentSearchResult> Rows, bool Complete, IReadOnlyList<IndexerRunTelemetry> Indexers);

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
    /// True when every indexer that was asked answered ("ok", or "trimmed" by the per-search cap: a deliberate, complete
    /// answer, 07d GOV-03 B), or was skipped only because it is disabled. A timeout, block, rate limit, breaker or
    /// parse error makes the answer partial.
    /// </summary>
    public static bool IsComplete(IReadOnlyList<IndexerRunTelemetry> runs) =>
        runs.All(run => run.Status is "ok" or "trimmed" || (run.Status == "skipped" && run.Detail == "disabled"));

    /// <summary>
    /// EZTV is searched by IMDb id: one URL per show answers every query of it. Background hunts keep that answer
    /// 2 hours instead of 45 minutes (doc 03 §1.7, 07d GOV-02); searches the user starts keep the normal TTL so a new
    /// episode shows up quickly.
    /// </summary>
    public static TimeSpan CacheTtlFor(string indexerId, SearchLane lane) =>
        indexerId == "eztv" && lane == SearchLane.Background ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(45);

    private async Task<NativeOutcome> SearchReducedAsync(SearchRecipe recipe, string query, IReadOnlyList<string> specifics, MediaKind kind, CancellationToken cancellationToken, SearchSubject? subject, SearchSession session)
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
            _http, _governor, _cache, id, searchLane, searchBudget.MaxGovernorWait, CacheTtlFor(id, searchLane), UserAgent, searchBudget.PerIndexer, session));
        var isAnime = IsAnimeRecipe(recipe);
        // TV: EZTV searches by IMDb id. Movies: YTS accepts the IMDb id for an exact lookup (plan 08 §Q2).
        var (imdbId, imdbFailed) = subject is not null ? await ResolveImdbIdAsync(subject, cancellationToken) : (null, false);
        var outcome = await orchestrator.SearchAsync(
            // Paged HTML indexers also get quality-bearing forms (plan 08: they reach deeper encodes).
            [
                new IndexerQuery(query, kind, isAnime, imdbId, subject?.Season, subject?.Episode),
                .. specifics.Select(specific => new IndexerQuery(specific, kind, isAnime, imdbId, subject?.Season, subject?.Episode, SpecificOnly: true))
            ],
            NativeIndexerCatalog.Create(settings),
            lane,
            budget,
            cancellationToken,
            // Kickass rows carry no hash: only rows the recipe could accept get their detail page opened.
            subject?.Evaluate is { } evaluate ? row => evaluate(row).IsAccepted : null);
        _logger.Info($"[Search] native query='{query}' kind={kind}{(isAnime ? " anime" : "")} lane={lane}: {outcome.Summary}", LogTarget.File | LogTarget.Console);
        // Without the IMDb id EZTV (IMDb-only) ran no query but still says "ok": do not memoize that answer.
        return new NativeOutcome(outcome.Results, IsComplete(outcome.Indexers) && !imdbFailed, outcome.Indexers);
    }

    private static async Task<T> TimedAsync<T>(
        Func<Task<T>> search,
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
        long qbitMs,
        string? qbitError,
        bool nativeFromMemo)
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
                native = new { rows = native.Keys.Count, good = native.Good.Count, excellent = native.Excellent, top = native.TopName, topScore = native.TopScore, ms = nativeMs, fromMemo = nativeFromMemo },
                qbittorrent = new { rows = qbit.Keys.Count, good = qbit.Good.Count, excellent = qbit.Excellent, top = qbit.TopName, topScore = qbit.TopScore, ms = qbitMs, error = qbitError },
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

    private async Task<(string? Id, bool Failed)> ResolveImdbIdAsync(SearchSubject subject, CancellationToken cancellationToken)
    {
        var key = $"{(subject.IsMovie ? "movie" : "tv")}:{subject.TmdbId}";
        // Lookups run one at a time; a queue of slow ones must not eat the search budget before it starts.
        if (!await _imdbGate.WaitAsync(ImdbLookupTimeout * 2, cancellationToken))
        {
            return (null, true);
        }

        try
        {
            if (_imdbIds.TryGetValue(key, out var cached))
            {
                return (cached, false);
            }

            if (_imdbMisses.TryGetValue(key, out var retryAt) && retryAt > DateTimeOffset.UtcNow)
            {
                return (null, false);
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
                return (null, failed);
            }

            _imdbMisses.Remove(key);

            _imdbIds[key] = imdbId;
            SaveImdbCache();
            return (imdbId, false);
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

    /// <summary>
    /// The recipe's backend, or qBittorrent while native search is off or paused. Inside a search session the first
    /// answer is kept for the whole run, so changing Settings mid-run cannot split it between backends (doc 06 §1).
    /// </summary>
    private string BackendFor(MediaKind kind, SearchRecipe? recipe = null) =>
        SessionScope.Value is { } session
            ? session.PinBackend($"{kind}|{recipe?.RecipeId}", () => ResolveBackend(recipe, session))
            : ResolveBackend(recipe, null);

    private string ResolveBackend(SearchRecipe? recipe, SearchSession? session)
    {
        var decision = SearchBackendResolver.Resolve(recipe, Settings.NativeEnabled, _governor.KillSwitch);
        if (decision.FallbackReason is { } reason)
        {
            session?.NoteBackendFallback(reason);
            var now = DateTimeOffset.UtcNow;
            var key = $"{recipe?.RecipeId}|{reason}";
            var logged = _fallbackLogged.TryGetValue(key, out var last) && now - last < FallbackLogInterval;
            if (!logged)
            {
                _fallbackLogged[key] = now;
                _logger.Warning($"[Search] {reason}", LogTarget.All);
            }
        }

        return decision.Effective;
    }

    private string StatePath => Path.Combine(_settingsService.Current.StateFolder, StateFileName);

    private bool _disposed;

    private void ScheduleSave()
    {
        lock (_saveGate)
        {
            if (_disposed)
            {
                return;
            }

            _saveTimer ??= new System.Threading.Timer(_ =>
            {
                lock (_saveGate)
                {
                    _firstUnsavedChange = null;
                }

                SaveState();
            }, null, Timeout.Infinite, Timeout.Infinite);
            var now = DateTimeOffset.UtcNow;
            _firstUnsavedChange ??= now;
            // A busy hunt reports every few hundred ms: without a cap the 2 s debounce never fires and a breaker
            // opened mid-hunt is lost on a crash (audit 07c NS-24).
            var due = now - _firstUnsavedChange.Value >= MaxSaveDelay ? TimeSpan.Zero : TimeSpan.FromSeconds(2);
            _saveTimer.Change(due, Timeout.InfiniteTimeSpan);
        }
    }

    private static readonly TimeSpan MaxSaveDelay = TimeSpan.FromSeconds(10);
    private DateTimeOffset? _firstUnsavedChange;

    private void SaveState()
    {
        // One save at a time: a timer callback and the final save on exit must not write the same .tmp (07d GOV-06).
        lock (_saveFileGate)
        {
            try
            {
                IndexerStateFile.Save(StatePath, _governor.Snapshot());
            }
            catch (Exception ex)
            {
                _logger.Warning($"[Search] could not save {StateFileName}: {ex.Message}", LogTarget.File);
            }
        }
    }

    public void Dispose()
    {
        System.Threading.Timer? timer;
        lock (_saveGate)
        {
            _disposed = true;
            timer = _saveTimer;
            _saveTimer = null;
        }

        if (timer is not null)
        {
            // Let a save that is already running finish before the final one.
            using var finished = new ManualResetEvent(false);
            if (timer.Dispose(finished))
            {
                finished.WaitOne(TimeSpan.FromSeconds(5));
            }
        }

        SaveState();
        _stateLock?.Dispose();
        _http.Dispose();
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
