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
    bool UsesNative(MediaKind kind);

    /// <summary>True when the qBittorrent search should also run for comparison (Search.Backend = Both).</summary>
    bool ComparesWithQbittorrent(MediaKind kind);

    /// <summary>Runs one query on the native indexers (governed, cached) and returns rows in the app's result shape.</summary>
    Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(SearchRecipe recipe, string query, MediaKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// Native search for a query; in "Both" mode also runs <paramref name="qbittorrentSearch"/> and logs a
    /// comparison. Returns native rows when native is selected, otherwise the qBittorrent rows.
    /// </summary>
    Task<IReadOnlyList<TorrentSearchResult>> SearchOrFallbackAsync(
        SearchRecipe recipe,
        string query,
        MediaKind kind,
        Func<Task<IReadOnlyList<TorrentSearchResult>>> qbittorrentSearch,
        CancellationToken cancellationToken);

    IReadOnlyList<IndexerStateSnapshot> HealthSnapshot();

    bool PauseAll { get; set; }
}

/// <summary>
/// App side of native search (native-search P2): builds the indexer set from settings, owns the request
/// governor (state persisted to &lt;StateFolder&gt;/indexer-state.json so limits survive restarts) and the
/// response cache, and routes each query according to the Search.Backend flag. Results are converted to
/// <see cref="TorrentSearchResult"/> so evaluation, dedupe, cart and the add gate stay unchanged.
/// </summary>
public sealed class NativeSearchService : INativeSearchService, IDisposable
{
    private const string StateFileName = "indexer-state.json";
    private const string UserAgent = "MediaManager/1.0 (+native-search)";
    private static readonly JsonSerializerOptions StateJson = new() { WriteIndented = true };
    private static readonly AsyncLocal<SearchLane?> LaneScope = new();

    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly HttpClient _http;
    private readonly IndexerResponseCache _cache = new();
    private readonly IndexerRequestGovernor _governor;
    private readonly object _saveGate = new();
    private System.Threading.Timer? _saveTimer;

    public NativeSearchService(ISettingsService settingsService, IAppLogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;
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

    public bool UsesNative(MediaKind kind) => BackendFor(kind) is SearchBackends.Native or SearchBackends.Both;

    public bool ComparesWithQbittorrent(MediaKind kind) => BackendFor(kind) == SearchBackends.Both;

    public IReadOnlyList<IndexerStateSnapshot> HealthSnapshot() => _governor.Snapshot();

    public async Task<IReadOnlyList<TorrentSearchResult>> SearchOrFallbackAsync(
        SearchRecipe recipe,
        string query,
        MediaKind kind,
        Func<Task<IReadOnlyList<TorrentSearchResult>>> qbittorrentSearch,
        CancellationToken cancellationToken)
    {
        if (!UsesNative(kind))
        {
            return await qbittorrentSearch();
        }

        if (!ComparesWithQbittorrent(kind))
        {
            return await SearchAsync(recipe, query, kind, cancellationToken);
        }

        var nativeTask = SearchAsync(recipe, query, kind, cancellationToken);
        var qbitTask = qbittorrentSearch();
        IReadOnlyList<TorrentSearchResult> qbitRows;
        try
        {
            qbitRows = await qbitTask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            qbitRows = [];
            _logger.Warning($"[Search] compare: qBittorrent search failed for '{query}': {ex.Message}", LogTarget.File);
        }

        var nativeRows = await nativeTask;
        var nativeKeys = nativeRows.Select(row => CandidateDeduplicator.IdentityKey(row.FileUrl)).ToHashSet(StringComparer.Ordinal);
        var qbitKeys = qbitRows.Select(row => CandidateDeduplicator.IdentityKey(row.FileUrl)).ToHashSet(StringComparer.Ordinal);
        _logger.Info(
            $"[Search] compare query='{query}' kind={kind}: native={nativeKeys.Count} qbittorrent={qbitKeys.Count} " +
            $"both={nativeKeys.Intersect(qbitKeys).Count()} nativeOnly={nativeKeys.Except(qbitKeys).Count()} qbittorrentOnly={qbitKeys.Except(nativeKeys).Count()}.",
            LogTarget.File | LogTarget.Console);
        return nativeRows;
    }

    public async Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(SearchRecipe recipe, string query, MediaKind kind, CancellationToken cancellationToken)
    {
        var settings = Settings;
        var lane = LaneScope.Value ?? SearchLane.Interactive;
        var budget = new SearchBudget(
            TimeSpan.FromSeconds(Math.Clamp(settings.PerIndexerTimeoutSeconds, 3, 60)),
            TimeSpan.FromSeconds(Math.Clamp(settings.OverallTimeoutSeconds, 5, 120)),
            lane == SearchLane.Background ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(10));
        foreach (var id in NativeIndexerCatalog.AllIds)
        {
            _governor.SetDisabled(id, settings.Indexers.TryGetValue(id, out var config) && !config.Enabled);
        }

        var orchestrator = new SearchOrchestrator(_governor, (id, searchLane, searchBudget) => new GovernedIndexerHttp(
            _http, _governor, _cache, id, searchLane, searchBudget.MaxGovernorWait, TimeSpan.FromMinutes(45), UserAgent));
        var isAnime = IsAnimeRecipe(recipe);
        var outcome = await orchestrator.SearchAsync(
            [new IndexerQuery(query, kind, isAnime)],
            NativeIndexerCatalog.Create(settings),
            lane,
            budget,
            cancellationToken);
        _logger.Info($"[Search] native query='{query}' kind={kind}{(isAnime ? " anime" : "")} lane={lane}: {outcome.Summary}", LogTarget.File | LogTarget.Console);
        return outcome.Results;
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

    private string BackendFor(MediaKind kind)
    {
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
