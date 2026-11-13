using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Search;
using media_management_app.Services;
using media_management_app.ViewModels;

namespace MediaManager.App.Tests.Search;

/// <summary>Quota pill: rows are updated in place (no rebuilt progress bars) and show the sites a running search uses.</summary>
public class SearchQuotaViewModelTests
{
    private sealed class FakeNativeSearch : INativeSearchService
    {
        public Dictionary<string, SiteActivity> Active { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> Used { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> InFlight { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Disabled { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool PauseAll { get; set; }

        public event Action? HealthChanged;

        public void RaiseHealthChanged() => HealthChanged?.Invoke();

        public bool UsesNative(MediaKind kind, SearchRecipe? recipe = null) => true;

        public bool ComparesWithQbittorrent(MediaKind kind, SearchRecipe? recipe = null) => false;

        public Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(SearchRecipe recipe, string query, MediaKind kind, CancellationToken cancellationToken, SearchSubject? subject = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TorrentSearchResult>> SearchOrFallbackAsync(
            SearchRecipe recipe,
            string query,
            MediaKind kind,
            Func<Task<IReadOnlyList<TorrentSearchResult>>> qbittorrentSearch,
            CancellationToken cancellationToken,
            SearchSubject? subject = null) =>
            throw new NotSupportedException();

        public IReadOnlyList<IndexerStateSnapshot> HealthSnapshot() =>
            Used.Keys.Union(InFlight.Keys, StringComparer.OrdinalIgnoreCase)
                .Select(id => new IndexerStateSnapshot
                {
                    IndexerId = id,
                    DayKey = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd"),
                    RequestsToday = Used.GetValueOrDefault(id),
                    InFlight = InFlight.GetValueOrDefault(id)
                })
                .ToList();

        public IReadOnlyDictionary<string, SiteActivity> ActiveSiteRuns() => new Dictionary<string, SiteActivity>(Active, StringComparer.OrdinalIgnoreCase);

        public IndexerPolicy PolicyFor(string indexerId) => IndexerPolicy.DefaultFor(indexerId);

        public bool IsIndexerEnabled(string indexerId) => !Disabled.Contains(indexerId);
    }

    [Fact]
    public void Refresh_KeepsEveryRowInstance_AndUpdatesItsValues()
    {
        var search = new FakeNativeSearch();
        var quota = new SearchQuotaViewModel(search);
        var before = quota.Sites.ToList();
        var nyaa = quota.Sites.Single(row => row.Site == "nyaa");
        var changed = new List<string?>();
        nyaa.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var collectionChanges = 0;
        quota.Sites.CollectionChanged += (_, _) => collectionChanges++;

        search.Used["nyaa"] = 250;
        quota.Refresh();

        Assert.Equal(before, quota.Sites);
        Assert.Equal(0, collectionChanges);
        Assert.Equal(250, nyaa.UsedToday);
        Assert.Equal($"250 / {nyaa.DailyBudget}", nyaa.UsageText);
        Assert.Equal(250.0 / nyaa.DailyBudget, nyaa.UsageFraction, 9);
        Assert.Contains(nameof(SearchQuotaRowViewModel.UsageFraction), changed);
    }

    [Fact]
    public void DisablingASite_MovesItsRow_WithoutReplacingIt()
    {
        var search = new FakeNativeSearch();
        var quota = new SearchQuotaViewModel(search);
        var apibay = quota.Sites.Single(row => row.Site == "apibay");

        search.Disabled.Add("apibay");
        quota.Refresh();

        Assert.Same(apibay, quota.Sites.Last());
        Assert.Equal(NativeIndexerCatalog.AllIds.Count, quota.Sites.Count);
    }

    [Fact]
    public void InUseMarks_FollowTheRunningSearches()
    {
        var search = new FakeNativeSearch();
        var quota = new SearchQuotaViewModel(search);
        Assert.Equal(0, quota.SitesInUse);
        Assert.All(quota.Sites, row => Assert.False(row.IsInUse));

        search.Active["nyaa"] = new SiteActivity(Interactive: 1, Background: 1);
        search.Active["yts"] = new SiteActivity(0, 1);
        search.InFlight["nyaa"] = 1;
        quota.Refresh();

        Assert.Equal(2, quota.SitesInUse);
        Assert.DoesNotContain("in use", quota.PillText);
        Assert.EndsWith("2 sites are in use by a running search.", quota.Summary);
        Assert.Equal(["nyaa", "yts"], quota.Sites.Where(row => row.IsInUse).Select(row => row.Site).Order());
        Assert.Equal("In use by Auto-Track and a manual search: 1 request in flight.", quota.Sites.Single(row => row.Site == "nyaa").InUseTooltip);

        search.Active.Clear();
        quota.Refresh();

        Assert.Equal(0, quota.SitesInUse);
        Assert.DoesNotContain("in use", quota.Summary);
        Assert.All(quota.Sites, row => Assert.False(row.IsInUse || row.InUseTooltip.Length > 0));
    }
}
