using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Search;
using media_management_app.Services;

namespace media_management_app.ViewModels;

/// <summary>Settings → Integrations → Search engines (native-search P2).</summary>
public partial class SettingsViewModel
{
    public const string SameAsDefaultBackend = "(same as default)";

    private INativeSearchService? _nativeSearch;

    public IReadOnlyList<string> SearchBackendOptions { get; } =
        [SearchBackends.Qbittorrent, SearchBackends.Native, SearchBackends.Both];

    public IReadOnlyList<string> SearchBackendOverrideOptions { get; } =
        [SameAsDefaultBackend, SearchBackends.Qbittorrent, SearchBackends.Native, SearchBackends.Both];

    public ObservableCollection<IndexerHealthRowViewModel> SearchIndexers { get; } = [];

    [ObservableProperty]
    private string searchBackend = SearchBackends.Qbittorrent;

    [ObservableProperty]
    private string searchBackendMovie = SameAsDefaultBackend;

    [ObservableProperty]
    private string searchBackendEpisode = SameAsDefaultBackend;

    [ObservableProperty]
    private string searchBackendPack = SameAsDefaultBackend;

    [ObservableProperty]
    private bool searchPauseAll;

    [ObservableProperty]
    private int searchPerIndexerTimeoutSeconds = 15;

    [ObservableProperty]
    private int searchOverallTimeoutSeconds = 90;

    public void AttachNativeSearch(INativeSearchService nativeSearch)
    {
        _nativeSearch = nativeSearch;
        LoadSearchSettings();
    }

    private void LoadSearchSettings()
    {
        var search = _settingsService.Current.Search ??= new NativeSearchSettings();
        SearchBackend = SearchBackendOptions.FirstOrDefault(option => string.Equals(option, search.Backend, StringComparison.OrdinalIgnoreCase))
                        ?? SearchBackends.Qbittorrent;
        SearchBackendMovie = OverrideFor(search, "Movie");
        SearchBackendEpisode = OverrideFor(search, "TvEpisode");
        SearchBackendPack = OverrideFor(search, "TvSeasonPack");
        // The governor's kill switch is the truth: a tick that was not saved is still in force (audit 07c NS-22).
        SearchPauseAll = _nativeSearch?.PauseAll ?? search.PauseAll;
        SearchPerIndexerTimeoutSeconds = search.PerIndexerTimeoutSeconds;
        SearchOverallTimeoutSeconds = search.OverallTimeoutSeconds;
        SearchIndexers.Clear();
        RefreshSearchHealth();
    }

    private void ApplySearchSettings()
    {
        var search = _settingsService.Current.Search ??= new NativeSearchSettings();
        search.Backend = SearchBackend;
        SetOverride(search, "Movie", SearchBackendMovie);
        SetOverride(search, "TvEpisode", SearchBackendEpisode);
        SetOverride(search, "TvSeasonPack", SearchBackendPack);
        search.PerIndexerTimeoutSeconds = Math.Clamp(SearchPerIndexerTimeoutSeconds, 3, 60);
        search.OverallTimeoutSeconds = Math.Clamp(SearchOverallTimeoutSeconds, 10, 600);
        foreach (var row in SearchIndexers)
        {
            if (!search.Indexers.TryGetValue(row.Id, out var config))
            {
                config = new IndexerSettings();
                search.Indexers[row.Id] = config;
            }

            config.Enabled = row.Enabled;
        }

        search.PauseAll = SearchPauseAll;
        if (_nativeSearch is not null)
        {
            _nativeSearch.PauseAll = SearchPauseAll;
        }
    }

    [RelayCommand]
    private void RefreshSearchHealth()
    {
        var search = _settingsService.Current.Search ??= new NativeSearchSettings();
        var health = (_nativeSearch?.HealthSnapshot() ?? []).ToDictionary(state => state.IndexerId, StringComparer.OrdinalIgnoreCase);
        // Refresh only updates health: ticks the user changed but did not save yet are kept.
        var unsaved = SearchIndexers.ToDictionary(row => row.Id, row => row.Enabled, StringComparer.OrdinalIgnoreCase);
        SearchIndexers.Clear();
        foreach (var id in NativeIndexerCatalog.AllIds)
        {
            health.TryGetValue(id, out var state);
            var policy = NativeIndexerCatalog.PolicyResolver(search)(id);
            SearchIndexers.Add(new IndexerHealthRowViewModel(
                id,
                unsaved.TryGetValue(id, out var enabled) ? enabled : NativeIndexerCatalog.IsEnabled(search, id),
                state is null ? "Not used yet" : DescribeState(state),
                state is null ? $"0 / {policy.DailyBudget} today" : $"{state.RequestsToday} / {policy.DailyBudget} today",
                state?.LastError ?? string.Empty));
        }
    }

    partial void OnSearchPauseAllChanged(bool value)
    {
        if (_nativeSearch is not null && !_isLoadingSettings)
        {
            _nativeSearch.PauseAll = value;
        }
    }

    private static string DescribeState(IndexerStateSnapshot state) => state.Breaker switch
    {
        IndexerBreakerState.Open when state.OpenUntilUtc is { } until && until > DateTimeOffset.UtcNow =>
            $"Paused until {until.ToLocalTime():HH:mm} (breaker open)",
        IndexerBreakerState.HalfOpen => "Testing again (half-open)",
        _ when state.CooldownUntilUtc is { } cooldown && cooldown > DateTimeOffset.UtcNow =>
            $"Cooling down until {cooldown.ToLocalTime():HH:mm:ss} (rate limited)",
        _ when state.LastSuccessUtc is { } ok => $"OK (last success {ok.ToLocalTime():dd/MM HH:mm})",
        _ => "No successful request yet"
    };

    private string OverrideFor(NativeSearchSettings search, string kind) =>
        search.BackendByKind.TryGetValue(kind, out var value) &&
        SearchBackendOptions.FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase)) is { } match
            ? match
            : SameAsDefaultBackend;

    private static void SetOverride(NativeSearchSettings search, string kind, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == SameAsDefaultBackend)
        {
            search.BackendByKind.Remove(kind);
        }
        else
        {
            search.BackendByKind[kind] = value;
        }
    }
}

public sealed partial class IndexerHealthRowViewModel(string id, bool enabled, string status, string usage, string lastError) : ObservableObject
{
    public string Id { get; } = id;

    public string Status { get; } = status;

    public string Usage { get; } = usage;

    public string LastError { get; } = lastError;

    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);

    [ObservableProperty]
    private bool enabled = enabled;
}
