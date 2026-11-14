using System.Diagnostics;
using FluentAssertions;
using media_management_app.Common;
using media_management_app.Search;

namespace MediaManager.Core.Tests.Search;

/// <summary>
/// Per-site time budget of one search: a site that is slow (or queued behind its own rate limit) is dropped from that
/// search at its budget, without making the other sites wait, and a site cooling down after a 429 is skipped instead
/// of being waited for. Real run 13-14/11: one site sending 3-5 requests held every search to the 90 s overall budget.
/// </summary>
public class SiteBudgetTests
{
    private static readonly IndexerPolicy Loose = new(RequestsPerMinute: 1000, MaxConcurrent: 4, MinInterval: TimeSpan.Zero, DailyBudget: 1000);

    private static readonly IndexerQuery Query = new("Movie 01", MediaKind.Movie);

    private sealed class NoHttp : IIndexerHttp
    {
        public Task<IndexerHttpResponse> GetAsync(Uri uri, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<IndexerHttpResponse> PostJsonAsync(Uri uri, string json, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    /// <summary>Answers one row at once, or hangs until its token is cancelled.</summary>
    private sealed class FakeIndexer(string id, bool hangs) : ITorrentIndexer
    {
        public string Id => id;

        public string EngineName => id;

        public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All);

        public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
        {
            if (hangs)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return
            [
                new IndexerResult($"Movie.01.2013.1080p.{id}", 1_000_000_000, 10, 1, "0123456789abcdef0123456789abcdef0123456" + (id == "fast" ? "a" : "b"), null, null, null, null, id)
            ];
        }
    }

    private static SearchOrchestrator Create(out IndexerRequestGovernor governor)
    {
        governor = new IndexerRequestGovernor(TimeProvider.System, _ => Loose);
        return new SearchOrchestrator(governor, (_, _, _) => new NoHttp());
    }

    [Fact]
    public async Task SlowSite_IsDroppedAtItsBudget_AndTheFastSiteIsNotMadeToWait()
    {
        var orchestrator = Create(out var governor);
        var budget = new SearchBudget(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), PerSite: TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();

        var outcome = await orchestrator.SearchAsync(
            [Query], [new FakeIndexer("fast", hangs: false), new FakeIndexer("slow", hangs: true)], SearchLane.Interactive, budget, CancellationToken.None);

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the overall budget is 30 s but the slow site only gets 0.3 s");
        var fast = outcome.Indexers.Single(run => run.IndexerId == "fast");
        var slow = outcome.Indexers.Single(run => run.IndexerId == "slow");
        fast.Status.Should().Be("ok");
        slow.Status.Should().Be("timeout");
        slow.Detail.Should().Contain("site budget");
        outcome.Results.Should().ContainSingle(row => row.FileName.EndsWith(".fast"));
        governor.ActiveSiteRuns().Should().BeEmpty();
    }

    /// <summary>Lists a row with a detail-page link only (no magnet), then hangs while reading the detail page.</summary>
    private sealed class DetailPageIndexer : ITorrentIndexer, IDetailPageResolver
    {
        public string Id => "pages";

        public string EngineName => "pages";

        public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All);

        public Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IndexerResult>>(
                [new IndexerResult("Movie.01.2013.1080p.pages", 1_000_000_000, 10, 1, null, null, null, "https://pages.example/movie-01-t1.html", null, "pages")]);

        public async Task<string?> ResolveMagnetAsync(string detailsUrl, IIndexerHttp http, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    [Fact]
    public async Task SiteCutDuringDetailLookups_DoesNotLeakRowsThatAreOnlyPageLinks()
    {
        var orchestrator = Create(out _);
        var budget = new SearchBudget(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), PerSite: TimeSpan.FromMilliseconds(300));

        var outcome = await orchestrator.SearchAsync(
            [Query], [new DetailPageIndexer()], SearchLane.Interactive, budget, CancellationToken.None,
            couldMatter: _ => true);

        outcome.Indexers.Single().Status.Should().Be("timeout");
        outcome.Results.Should().BeEmpty("a detail-page link is not something qBittorrent can add");
    }

    [Fact]
    public async Task SiteBudget_IsNotTheOverallBudget_InTheTelemetry()
    {
        var orchestrator = Create(out _);
        var overall = new SearchBudget(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(30), PerSite: TimeSpan.FromSeconds(30));

        var outcome = await orchestrator.SearchAsync([Query], [new FakeIndexer("slow", hangs: true)], SearchLane.Interactive, overall, CancellationToken.None);

        outcome.Indexers.Single().Detail.Should().Contain("search budget").And.NotContain("site budget");
    }

    [Fact]
    public void Defaults_Interactive15Seconds_BackgroundWider()
    {
        SearchBudget.Interactive.PerSite.Should().Be(TimeSpan.FromSeconds(15));
        SearchBudget.Background.PerSite.Should().BeGreaterThan(SearchBudget.Interactive.PerSite!.Value);
    }

    [Fact]
    public async Task SiteCoolingDownAfter429_IsNotWaitedFor_ByARequest()
    {
        var governor = new IndexerRequestGovernor(TimeProvider.System, _ => Loose);
        using (await governor.AcquireAsync("site", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None))
        {
            governor.Report("site", IndexerFailureKind.RateLimited, TimeSpan.FromSeconds(60), "HTTP 429");
        }

        // Even with 5 minutes to spare, a request does not queue behind a 60 s Retry-After: it is refused at once.
        var acquire = governor.AcquireAsync("site", SearchLane.Background, TimeSpan.FromMinutes(5), CancellationToken.None);
        var refused = await Assert.ThrowsAsync<IndexerUnavailableException>(() => acquire.WaitAsync(TimeSpan.FromSeconds(3)));
        refused.Reason.Should().Contain("cooling down");
    }

    [Fact]
    public async Task SiteCoolingDownAfter429_IsSkippedBySearch_AndTheOthersAnswer()
    {
        var orchestrator = Create(out var governor);
        using (await governor.AcquireAsync("slow", SearchLane.Background, TimeSpan.Zero, CancellationToken.None))
        {
            governor.Report("slow", IndexerFailureKind.RateLimited, TimeSpan.FromSeconds(60), "HTTP 429");
        }

        var clock = Stopwatch.StartNew();
        var outcome = await orchestrator.SearchAsync(
            [Query], [new FakeIndexer("fast", hangs: false), new FakeIndexer("slow", hangs: true)], SearchLane.Background, SearchBudget.Background, CancellationToken.None);

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        outcome.Indexers.Single(run => run.IndexerId == "slow").Status.Should().Be("skipped");
        outcome.Indexers.Single(run => run.IndexerId == "slow").Detail.Should().Contain("cooling down");
        outcome.Indexers.Single(run => run.IndexerId == "fast").Status.Should().Be("ok");
    }
}
