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

    /// <summary>Sites a running search is using now (<see cref="INativeSearchService.ActiveSiteRuns"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSitesInUse))]
    private int sitesInUse;

    public bool HasSitesInUse => SitesInUse > 0;

    [RelayCommand]
    public void Refresh()
    {
        var now = DateTimeOffset.UtcNow;
        var states = _nativeSearch.HealthSnapshot().ToDictionary(state => state.IndexerId, StringComparer.OrdinalIgnoreCase);
        var activity = _nativeSearch.ActiveSiteRuns();
        var paused = _nativeSearch.PauseAll;
        var existing = Sites.ToDictionary(row => row.Site, StringComparer.OrdinalIgnoreCase);
        var rows = NativeIndexerCatalog.AllIds
            .Select(id =>
            {
                states.TryGetValue(id, out var state);
                activity.TryGetValue(id, out var running);
                var policy = _nativeSearch.PolicyFor(id);
                var status = NativeSearchHealth.Describe(state, policy, _nativeSearch.IsIndexerEnabled(id), paused, now);
                // Keep each row's instance: replacing it rebuilds the row, and the progress bar of a new row plays its
                // load animation from 0 on every refresh.
                if (!existing.TryGetValue(id, out var row))
                {
                    row = new SearchQuotaRowViewModel(id);
                }

                row.Update(status, policy.DailyBudget, NativeSearchHealth.BackgroundBudget(policy), running, state?.InFlight ?? 0);
                return row;
            })
            .OrderBy(row => row.Severity == SearchSiteSeverity.Off)
            .ThenBy(row => row.Site, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SyncSites(rows);

        var active = rows.Where(row => row.Severity != SearchSiteSeverity.Off).ToList();
        var ready = active.Count(row => row.Severity == SearchSiteSeverity.Ok);
        var skipped = active.Count(row => row.Severity == SearchSiteSeverity.Unavailable);
        var limited = active.Count(row => row.Severity == SearchSiteSeverity.Limited);
        SitesInUse = rows.Count(row => row.IsInUse);
        IsNativeInUse = _nativeSearch.NativeEnabled;
        Severity = active.Count == 0 || skipped == active.Count
            ? SearchSiteSeverity.Unavailable
            : skipped > 0 || limited > 0 ? SearchSiteSeverity.Limited : SearchSiteSeverity.Ok;
        // Same width whether or not a search runs (the pill's position is fixed, c03af85): "in use" shows as the radar
        // icon's colour on the pill, and as the count in its tooltip and in the popup.
        PillText = paused ? "Search paused" : $"Search {ready}/{active.Count}";
        Summary = (paused
            ? "All native searches are paused (Settings → Search engines)."
            : $"{ready} of {active.Count} sites ready" +
              (skipped > 0 ? $", {skipped} skipped" : string.Empty) +
              (limited > 0 ? $", {limited} limited" : string.Empty) + ".") +
            (SitesInUse > 0 ? $" {SitesInUse} {(SitesInUse == 1 ? "site is" : "sites are")} in use by a running search." : string.Empty);
        BackendLine = IsNativeInUse
            ? "Native search is on: recipes set to Native or Both use these sites."
            : "Native search is off (Settings → Search engines): every recipe searches with qBittorrent.";
        ResetLine = $"Daily limits reset at {NativeSearchHealth.NextResetLocal(now):HH:mm}. Auto-Track may use 70% of each limit; the rest is kept for manual searches.";
    }

    /// <summary>Brings <see cref="Sites"/> to <paramref name="rows"/> (same instances) with the fewest changes.</summary>
    private void SyncSites(IReadOnlyList<SearchQuotaRowViewModel> rows)
    {
        for (var index = Sites.Count - 1; index >= 0; index--)
        {
            if (!rows.Contains(Sites[index]))
            {
                Sites.RemoveAt(index);
            }
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var current = Sites.IndexOf(rows[index]);
            if (current < 0)
            {
                Sites.Insert(index, rows[index]);
            }
            else if (current != index)
            {
                Sites.Move(current, index);
            }
        }
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

/// <summary>One site of the quota popup. Updated in place on every refresh (see <see cref="SearchQuotaViewModel.Refresh"/>).</summary>
public sealed partial class SearchQuotaRowViewModel(string site) : ObservableObject
{
    public string Site { get; } = site;

    [ObservableProperty]
    private string status = string.Empty;

    [ObservableProperty]
    private SearchSiteSeverity severity;

    [ObservableProperty]
    private int usedToday;

    [ObservableProperty]
    private int dailyBudget;

    [ObservableProperty]
    private string usageText = string.Empty;

    [ObservableProperty]
    private string usageTooltip = string.Empty;

    [ObservableProperty]
    private double usageFraction;

    /// <summary>A running search has an unfinished run on this site (<see cref="SiteActivity"/>).</summary>
    [ObservableProperty]
    private bool isInUse;

    [ObservableProperty]
    private string inUseTooltip = string.Empty;

    public void Update(SearchSiteStatus siteStatus, int budget, int backgroundBudget, SiteActivity running, int inFlight)
    {
        Status = siteStatus.Text;
        Severity = siteStatus.Severity;
        UsedToday = siteStatus.UsedToday;
        DailyBudget = budget;
        UsageText = $"{siteStatus.UsedToday} / {budget}";
        UsageTooltip = $"{siteStatus.UsedToday} of {budget} requests today; Auto-Track may use {backgroundBudget}.";
        UsageFraction = budget <= 0 ? 0 : Math.Min(1, (double)siteStatus.UsedToday / budget);
        IsInUse = running.Total > 0;
        InUseTooltip = IsInUse ? DescribeUse(running, inFlight) : string.Empty;
    }

    private static string DescribeUse(SiteActivity running, int inFlight)
    {
        var who = new List<string>(2);
        if (running.Background > 0)
        {
            who.Add(running.Background == 1 ? "Auto-Track" : $"Auto-Track ×{running.Background}");
        }

        if (running.Interactive > 0)
        {
            who.Add(running.Interactive == 1 ? "a manual search" : $"{running.Interactive} manual searches");
        }

        var requests = inFlight switch
        {
            0 => "waiting for its turn or reading the answer",
            1 => "1 request in flight",
            _ => $"{inFlight} requests in flight"
        };
        return $"In use by {string.Join(" and ", who)}: {requests}.";
    }
}
