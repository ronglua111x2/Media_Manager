using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Search;
using media_management_app.Services;

namespace media_management_app.ViewModels;

/// <summary>Settings → Integrations → Search engines (native-search P2).</summary>
public partial class SettingsViewModel
{
    private INativeSearchService? _nativeSearch;

    public ObservableCollection<IndexerHealthRowViewModel> SearchIndexers { get; } = [];

    /// <summary>
    /// Master switch of native search. Each recipe chooses its backend (Search module); while this is off a recipe
    /// set to Native or Both searches with qBittorrent and says so in the log and on the order.
    /// </summary>
    [ObservableProperty]
    private bool searchNativeEnabled;

    [ObservableProperty]
    private bool searchPauseAll;

    [ObservableProperty]
    private int searchPerIndexerTimeoutSeconds = 15;

    [ObservableProperty]
    private int searchOverallTimeoutSeconds = 90;

    [ObservableProperty]
    private int searchSiteBudgetSeconds = 15;

    [ObservableProperty]
    private int searchBackgroundSiteBudgetSeconds = 60;

    public void AttachNativeSearch(INativeSearchService nativeSearch)
    {
        _nativeSearch = nativeSearch;
        LoadSearchSettings();
    }

    private void LoadSearchSettings()
    {
        var search = _settingsService.Current.Search ??= new NativeSearchSettings();
        SearchNativeEnabled = search.NativeEnabled;
        // The governor's kill switch is the truth: a tick that was not saved is still in force (audit 07c NS-22).
        SearchPauseAll = _nativeSearch?.PauseAll ?? search.PauseAll;
        SearchPerIndexerTimeoutSeconds = search.PerIndexerTimeoutSeconds;
        SearchOverallTimeoutSeconds = search.OverallTimeoutSeconds;
        SearchSiteBudgetSeconds = search.SiteBudgetSeconds;
        SearchBackgroundSiteBudgetSeconds = search.BackgroundSiteBudgetSeconds;
        SearchIndexers.Clear();
        RefreshSearchHealth();
    }

    private void ApplySearchSettings()
    {
        var search = _settingsService.Current.Search ??= new NativeSearchSettings();
        // Search.Backend / BackendByKind are legacy (moved into the recipes once) and are no longer written.
        search.NativeEnabled = SearchNativeEnabled;
        search.PerIndexerTimeoutSeconds = Math.Clamp(SearchPerIndexerTimeoutSeconds, 3, 60);
        search.OverallTimeoutSeconds = Math.Clamp(SearchOverallTimeoutSeconds, 10, 600);
        search.SiteBudgetSeconds = Math.Clamp(SearchSiteBudgetSeconds, 5, 120);
        search.BackgroundSiteBudgetSeconds = Math.Clamp(SearchBackgroundSiteBudgetSeconds, 10, 300);
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
                NativeSearchHealth.Describe(
                    state,
                    policy,
                    unsaved.TryGetValue(id, out var shownEnabled) ? shownEnabled : NativeIndexerCatalog.IsEnabled(search, id),
                    _nativeSearch?.PauseAll ?? search.PauseAll,
                    DateTimeOffset.UtcNow).Text,
                $"{NativeSearchHealth.UsedToday(state, DateTimeOffset.UtcNow)} / {policy.DailyBudget} today",
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
