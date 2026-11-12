using System.Net;
using FluentAssertions;
using media_management_app.Common;
using media_management_app.Search;

namespace MediaManager.Core.Tests.Search;

/// <summary>Audit 07c NS-04 (success reported after parsing), NS-06 (per-caller waits), NS-15 (catch-all).</summary>
public class GovernedHttpAndOrchestratorTests
{
    private static readonly IndexerPolicy Loose = new(RequestsPerMinute: 1000, MaxConcurrent: 4, MinInterval: TimeSpan.Zero, DailyBudget: 1000);

    private sealed class StubHandler(Func<HttpRequestMessage, string> body) : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body(request), System.Text.Encoding.UTF8, "text/html"),
                RequestMessage = request
            });
        }
    }

    private static (GovernedIndexerHttp Http, IndexerRequestGovernor Governor, IndexerResponseCache Cache, StubHandler Handler) Create()
    {
        var handler = new StubHandler(_ => "<html><body>Access denied</body></html>");
        var governor = new IndexerRequestGovernor(TimeProvider.System, _ => Loose);
        var cache = new IndexerResponseCache();
        var http = new GovernedIndexerHttp(new HttpClient(handler), governor, cache, "site", SearchLane.Interactive, TimeSpan.Zero, TimeSpan.FromMinutes(45), "test");
        return (http, governor, cache, handler);
    }

    private static IndexerStateSnapshot State(IndexerRequestGovernor governor) => governor.Snapshot().Single(state => state.IndexerId == "site");

    [Fact]
    public async Task UnparseablePagesServedWith200_OpenTheBreaker()
    {
        var (http, governor, _, _) = Create();

        for (var i = 0; i < 3; i++)
        {
            await http.GetAsync(new Uri($"https://site.test/search/{i}"), CancellationToken.None);
            http.ReportParsed(false);
        }

        State(governor).Breaker.Should().Be(IndexerBreakerState.Open);
        governor.GetAvailability("site").CanRun.Should().BeFalse();
    }

    [Fact]
    public async Task ParsedResponse_ReportsSuccess_AndCacheHitReportsNothing()
    {
        var (http, governor, cache, handler) = Create();
        var uri = new Uri("https://site.test/search/a");

        await http.GetAsync(uri, CancellationToken.None);
        http.ReportParsed(true);
        State(governor).LastSuccessUtc.Should().NotBeNull();

        // Served from the cache: the site was not contacted, so a parse failure is not the site's fault...
        await http.GetAsync(uri, CancellationToken.None);
        handler.Calls.Should().Be(1);
        http.ReportParsed(false);
        State(governor).ConsecutiveFailures.Should().Be(0);

        // ...but the response is dropped, so the next search asks the site again.
        cache.Count.Should().Be(0);
        await http.GetAsync(uri, CancellationToken.None);
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task JoinedCaller_IsNotCancelledWhenTheStarterIs()
    {
        var cache = new IndexerResponseCache();
        using var starter = new CancellationTokenSource();
        var response = new IndexerHttpResponse(200, "joined", "text/html", new Uri("https://site.test/"));

        var first = cache.GetOrAddAsync("key", TimeSpan.FromMinutes(1), async () =>
        {
            await Task.Delay(Timeout.Infinite, starter.Token);
            return response;
        }, starter.Token);
        var second = cache.GetOrAddAsync("key", TimeSpan.FromMinutes(1), () => Task.FromResult(response), CancellationToken.None);

        starter.Cancel();

        await first.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        (await second).Body.Should().Be("joined");
    }

    [Fact]
    public async Task JoinedCaller_CanStopWaitingWithItsOwnToken()
    {
        var cache = new IndexerResponseCache();
        var gate = new TaskCompletionSource<IndexerHttpResponse>();
        using var joiner = new CancellationTokenSource();

        var first = cache.GetOrAddAsync("key", TimeSpan.FromMinutes(1), () => gate.Task, CancellationToken.None);
        var second = cache.GetOrAddAsync("key", TimeSpan.FromMinutes(1), () => gate.Task, joiner.Token);
        joiner.Cancel();

        await second.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        gate.SetResult(new IndexerHttpResponse(200, "done", null, new Uri("https://site.test/")));
        (await first).Body.Should().Be("done");
    }

    private sealed class FakeIndexer(string id, Func<IReadOnlyList<IndexerResult>> search) : ITorrentIndexer
    {
        public string Id => id;

        public string EngineName => id;

        public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All);

        public Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken) =>
            Task.FromResult(search());
    }

    private sealed class NoHttp : IIndexerHttp
    {
        public Task<IndexerHttpResponse> GetAsync(Uri uri, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<IndexerHttpResponse> PostJsonAsync(Uri uri, string json, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    [Fact]
    public async Task OneIndexerThrowingAnUnexpectedException_DoesNotLoseTheOthersRows()
    {
        var governor = new IndexerRequestGovernor(TimeProvider.System, _ => Loose);
        var orchestrator = new SearchOrchestrator(governor, (_, _, _) => new NoHttp());
        var good = new FakeIndexer("good", () =>
        [
            new IndexerResult("Coherence 2013 1080p BluRay x264", 1_500_000_000, 10, 1, "bd9c339c8105439135b8938633db632b4df51fbb", null, null, null, null, "good")
        ]);
        var broken = new FakeIndexer("broken", () => throw new ArgumentOutOfRangeException("seconds"));

        var outcome = await orchestrator.SearchAsync(
            [new IndexerQuery("Coherence", MediaKind.Movie)], [good, broken], SearchLane.Interactive, SearchBudget.Interactive, CancellationToken.None);

        outcome.Results.Should().ContainSingle();
        outcome.Indexers.Single(run => run.IndexerId == "broken").Status.Should().Be("parse-error");
    }
}
