using FluentAssertions;
using media_management_app.Common;
using media_management_app.Search;

namespace MediaManager.Core.Tests.Search;

/// <summary>
/// "In use" marks of the quota pill: a site is in use while a running search has an unfinished run on it
/// (<see cref="IndexerRequestGovernor.BeginSiteRun"/>, held by <see cref="SearchOrchestrator"/>).
/// </summary>
public class SiteActivityTests
{
    private static readonly IndexerPolicy Loose = new(RequestsPerMinute: 1000, MaxConcurrent: 4, MinInterval: TimeSpan.Zero, DailyBudget: 1000);

    private static readonly IndexerQuery Query = new("Movie 01", MediaKind.Movie);

    /// <summary>Waits until the test releases it (or the search token is cancelled), then answers, fails or throws.</summary>
    private sealed class GatedIndexer(string id) : ITorrentIndexer
    {
        private readonly TaskCompletionSource<Func<IReadOnlyList<IndexerResult>>> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => id;

        public string EngineName => id;

        public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All);

        public Task Entered => _entered.Task;

        public void Answer() => _gate.TrySetResult(() => []);

        public void Throw(Exception error) => _gate.TrySetResult(() => throw error);

        public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            var result = await _gate.Task.WaitAsync(cancellationToken);
            return result();
        }
    }

    private sealed class NoHttp : IIndexerHttp
    {
        public Task<IndexerHttpResponse> GetAsync(Uri uri, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<IndexerHttpResponse> PostJsonAsync(Uri uri, string json, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private static (SearchOrchestrator Orchestrator, IndexerRequestGovernor Governor) Create()
    {
        var governor = new IndexerRequestGovernor(TimeProvider.System, _ => Loose);
        return (new SearchOrchestrator(governor, (_, _, _) => new NoHttp()), governor);
    }

    private static Task<OrchestratedSearch> Search(
        SearchOrchestrator orchestrator,
        IReadOnlyList<ITorrentIndexer> indexers,
        SearchLane lane = SearchLane.Interactive,
        SearchBudget? budget = null,
        CancellationToken cancellationToken = default) =>
        orchestrator.SearchAsync([Query], indexers, lane, budget ?? SearchBudget.Interactive, cancellationToken);

    [Fact]
    public async Task SiteIsInUse_FromStartUntilItsRunFinishes()
    {
        var (orchestrator, governor) = Create();
        var site = new GatedIndexer("site");
        var changes = 0;
        governor.ActivityChanged += () => Interlocked.Increment(ref changes);

        governor.ActiveSiteRuns().Should().BeEmpty();
        var search = Search(orchestrator, [site]);
        await site.Entered;

        governor.ActiveSiteRuns().Should().ContainKey("site").WhoseValue.Should().Be(new SiteActivity(1, 0));

        site.Answer();
        (await search).Indexers.Single().Status.Should().Be("ok");
        governor.ActiveSiteRuns().Should().BeEmpty();
        changes.Should().Be(2, "one change when the site starts, one when it is done");
    }

    [Fact]
    public async Task CancelledSearch_ClearsItsSites()
    {
        var (orchestrator, governor) = Create();
        var first = new GatedIndexer("first");
        var second = new GatedIndexer("second");
        using var cancel = new CancellationTokenSource();

        var search = Search(orchestrator, [first, second], cancellationToken: cancel.Token);
        await Task.WhenAll(first.Entered, second.Entered);
        governor.ActiveSiteRuns().Keys.Should().BeEquivalentTo(["first", "second"]);

        cancel.Cancel();
        await search.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        governor.ActiveSiteRuns().Should().BeEmpty();
    }

    [Theory]
    [InlineData("parse")]
    [InlineData("http")]
    [InlineData("blocked")]
    [InlineData("unavailable")]
    public async Task FailingSite_IsClearedWhenItFails(string failure)
    {
        var (orchestrator, governor) = Create();
        var site = new GatedIndexer("site");

        var search = Search(orchestrator, [site]);
        await site.Entered;
        site.Throw(failure switch
        {
            "parse" => new FormatException("layout changed"),
            "http" => new IndexerHttpException("site", IndexerFailureKind.ServerError, "HTTP 502"),
            "blocked" => new IndexerBlockedException("site", "HTTP 403"),
            _ => new IndexerUnavailableException("site", "breaker open")
        });

        (await search).Indexers.Single().Status.Should().NotBe("ok");
        governor.ActiveSiteRuns().Should().BeEmpty();
    }

    [Fact]
    public async Task SearchBudgetRunningOut_ClearsTheSite()
    {
        var (orchestrator, governor) = Create();
        var site = new GatedIndexer("site");

        var outcome = await Search(orchestrator, [site], budget: new SearchBudget(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));

        outcome.Indexers.Single().Status.Should().Be("timeout");
        governor.ActiveSiteRuns().Should().BeEmpty();
    }

    [Fact]
    public async Task FastSite_IsClearedWhileASlowSiteOfTheSameSearchIsStillRunning()
    {
        var (orchestrator, governor) = Create();
        var fast = new GatedIndexer("fast");
        var slow = new GatedIndexer("slow");

        var search = Search(orchestrator, [fast, slow]);
        await Task.WhenAll(fast.Entered, slow.Entered);
        fast.Answer();
        await WaitUntil(() => !governor.ActiveSiteRuns().ContainsKey("fast"));

        governor.ActiveSiteRuns().Keys.Should().BeEquivalentTo(["slow"]);
        slow.Answer();
        await search;
        governor.ActiveSiteRuns().Should().BeEmpty();
    }

    [Fact]
    public async Task OverlappingSearches_AreCountedPerLane_AndEachEndsOnlyItsOwnRun()
    {
        var (orchestrator, governor) = Create();
        var huntSite = new GatedIndexer("site");
        var cartSite = new GatedIndexer("site");
        var cartOnly = new GatedIndexer("other");

        var hunt = Search(orchestrator, [huntSite], SearchLane.Background, SearchBudget.Background);
        var cart = Search(orchestrator, [cartSite, cartOnly]);
        await Task.WhenAll(huntSite.Entered, cartSite.Entered, cartOnly.Entered);

        var active = governor.ActiveSiteRuns();
        active["site"].Should().Be(new SiteActivity(Interactive: 1, Background: 1));
        active["site"].Total.Should().Be(2);
        active["other"].Should().Be(new SiteActivity(1, 0));

        cartSite.Answer();
        cartOnly.Answer();
        await cart;
        governor.ActiveSiteRuns().Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, SiteActivity>("site", new SiteActivity(0, 1)));

        huntSite.Answer();
        await hunt;
        governor.ActiveSiteRuns().Should().BeEmpty();
    }

    [Fact]
    public async Task SkippedSite_IsNeverMarked()
    {
        var (orchestrator, governor) = Create();
        governor.SetDisabled("off", true);
        var marked = false;
        governor.ActivityChanged += () => marked |= governor.ActiveSiteRuns().ContainsKey("off");

        var outcome = await Search(orchestrator, [new GatedIndexer("off")]);

        outcome.Indexers.Single().Status.Should().Be("skipped");
        marked.Should().BeFalse();
    }

    [Fact]
    public void Handle_DisposedTwice_EndsOnlyOneRun()
    {
        var governor = new IndexerRequestGovernor(TimeProvider.System, _ => Loose);
        var first = governor.BeginSiteRun("site", SearchLane.Interactive);
        using var second = governor.BeginSiteRun("site", SearchLane.Interactive);

        first.Dispose();
        first.Dispose();

        governor.ActiveSiteRuns()["site"].Should().Be(new SiteActivity(1, 0));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }
}
