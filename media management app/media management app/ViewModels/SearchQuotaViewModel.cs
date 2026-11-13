using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Search;
using media_management_app.Services;

namespace media_management_app.ViewModels;

/// <summary>
/// Native search quota for every site, shown as a pill on the Auto, News and Cart screens (audit 07c: a skipped
/// site used to be visible only in the log). One shared instance; refreshed when the governor's state changes and
/// every 30 s for countdowns.
/// </summary>
public sealed partial class SearchQuotaViewModel : ObservableObject
{
    private readonly INativeSearchService _nativeSearch;
    private readonly DispatcherTimer _timer;
    private int _refreshQueued;

    public SearchQuotaViewModel(INativeSearchService nativeSearch)
    {
        _nativeSearch = nativeSearch;
        _nativeSearch.HealthChanged += QueueRefresh;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    public ObservableCollection<SearchQuotaRowViewModel> Sites { get; } = [];

    [ObservableProperty]
    private string pillText = "Search";

    [ObservableProperty]
    private string summary = string.Empty;

    [ObservableProperty]
    private string backendLine = string.Empty;

    [ObservableProperty]
    private string resetLine = string.Empty;

    [ObservableProperty]
    private SearchSiteSeverity severity = SearchSiteSeverity.Ok;

    [ObservableProperty]
    private bool isNativeInUse;

    [RelayCommand]
    public void Refresh()
    {
        var now = DateTimeOffset.UtcNow;
        var states = _nativeSearch.HealthSnapshot().ToDictionary(state => state.IndexerId, StringComparer.OrdinalIgnoreCase);
        var paused = _nativeSearch.PauseAll;
        var rows = NativeIndexerCatalog.AllIds
            .Select(id =>
            {
                states.TryGetValue(id, out var state);
                var policy = _nativeSearch.PolicyFor(id);
                var status = NativeSearchHealth.Describe(state, policy, _nativeSearch.IsIndexerEnabled(id), paused, now);
                return new SearchQuotaRowViewModel(id, status, policy.DailyBudget, NativeSearchHealth.BackgroundBudget(policy));
            })
            .OrderBy(row => row.Severity == SearchSiteSeverity.Off)
            .ThenBy(row => row.Site, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Sites.Clear();
        foreach (var row in rows)
        {
            Sites.Add(row);
        }

        var active = rows.Where(row => row.Severity != SearchSiteSeverity.Off).ToList();
        var ready = active.Count(row => row.Severity == SearchSiteSeverity.Ok);
        var skipped = active.Count(row => row.Severity == SearchSiteSeverity.Unavailable);
        var limited = active.Count(row => row.Severity == SearchSiteSeverity.Limited);
        IsNativeInUse = _nativeSearch.NativeEnabled;
        Severity = active.Count == 0 || skipped == active.Count
            ? SearchSiteSeverity.Unavailable
            : skipped > 0 || limited > 0 ? SearchSiteSeverity.Limited : SearchSiteSeverity.Ok;
        PillText = paused ? "Search paused" : $"Search {ready}/{active.Count}";
        Summary = paused
            ? "All native searches are paused (Settings → Search engines)."
            : $"{ready} of {active.Count} sites ready" +
              (skipped > 0 ? $", {skipped} skipped" : string.Empty) +
              (limited > 0 ? $", {limited} limited" : string.Empty) + ".";
        BackendLine = IsNativeInUse
            ? "Native search is on: recipes set to Native or Both use these sites."
            : "Native search is off (Settings → Search engines): every recipe searches with qBittorrent.";
        ResetLine = $"Daily limits reset at {NativeSearchHealth.NextResetLocal(now):HH:mm}. Auto-Track may use 70% of each limit; the rest is kept for manual searches.";
    }

    private void QueueRefresh()
    {
        // The governor reports from search threads, several times per second during a hunt: refresh once per burst.
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            Refresh();
        });
    }
}

public sealed class SearchQuotaRowViewModel(string site, SearchSiteStatus status, int dailyBudget, int backgroundBudget)
{
    public string Site { get; } = site;

    public string Status { get; } = status.Text;

    public SearchSiteSeverity Severity { get; } = status.Severity;

    public int UsedToday { get; } = status.UsedToday;

    public int DailyBudget { get; } = dailyBudget;

    public string UsageText { get; } = $"{status.UsedToday} / {dailyBudget}";

    public string UsageTooltip { get; } = $"{status.UsedToday} of {dailyBudget} requests today; Auto-Track may use {backgroundBudget}.";

    public double UsageFraction { get; } = dailyBudget <= 0 ? 0 : Math.Min(1, (double)status.UsedToday / dailyBudget);
}
