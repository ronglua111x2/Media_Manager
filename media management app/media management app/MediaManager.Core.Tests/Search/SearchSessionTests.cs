using System.Net;
using FluentAssertions;
using media_management_app.Common;
using media_management_app.Search;

namespace MediaManager.Core.Tests.Search;

/// <summary>
/// Audit 07d GOV-03 B (per-search request cap, low-budget mode, cache hits free), GOV-08 (request/cache-hit telemetry)
/// and the "no search source available" outcome of a search session.
/// </summary>
public class SearchSessionTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string> body) : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body(request), System.Text.Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }

    /// <summary>One request per query (or per page): returns one row per request, named after the query and page.</summary>
    private sealed class FakeIndexer(int pages = 1) : ITorrentIndexer
    {
        public string Id => "site";

        public string EngineName => "site";

        public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All, PrefersSpecificQueries: true);

        public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
        {
            var rows = new List<IndexerResult>();
            for (var page = 1; page <= pages; page++)
            {
                IndexerHttpResponse response;
                try
                {
                    response = await http.GetAsync(new Uri($"https://site.test/{Uri.EscapeDataString(query.Text)}/{page}"), cancellationToken);
                }
                catch (IndexerSearchCapException) when (page > 1)
                {
                    break;
                }

                var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(response.Body))).ToLowerInvariant();
                rows.Add(new IndexerResult(response.Body, 1, 5, 0, hash, null, null, null, null, Id));
            }

            return rows;
        }
    }

    private static readonly IndexerPolicy Loose = new(RequestsPerMinute: 1000, MaxConcurrent: 4, MinInterval: TimeSpan.Zero, DailyBudget: 1000, MaxRequestsPerSearch: 2);

    private static (SearchOrchestrator Orchestrator, StubHandler Handler, IndexerRequestGovernor Governor) Create(SearchSession session, IndexerPolicy? policy = null)
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath);
        var governor = new IndexerRequestGovernor(TimeProvider.System, _ => policy ?? Loose);
        var cache = new IndexerResponseCache();
        var client = new HttpClient(handler);
        var orchestrator = new SearchOrchestrator(governor, (id, lane, budget) =>
            new GovernedIndexerHttp(client, governor, cache, id, lane, budget.MaxGovernorWait, TimeSpan.FromMinutes(45), "test", budget.PerIndexer, session));
        return (orchestrator, handler, governor);
    }

    private static IndexerQuery Query(string text, bool specific = false) => new(text, MediaKind.Movie, SpecificOnly: specific);

    [Fact]
    public async Task PerSearchCap_TrimsLaterQueries_AndKeepsTheRowsFound()
    {
        var session = new SearchSession();
        var (orchestrator, handler, _) = Create(session);

        // Background lane: cap 2. Three distinct queries in one search target.
        var first = await orchestrator.SearchAsync([Query("a"), Query("b")], [new FakeIndexer()], SearchLane.Background, SearchBudget.Background, CancellationToken.None);
        var second = await orchestrator.SearchAsync([Query("c")], [new FakeIndexer()], SearchLane.Background, SearchBudget.Background, CancellationToken.None);

        handler.Calls.Should().Be(2);
        first.Results.Should().HaveCount(2);
        first.Indexers.Single().Should().Match<IndexerRunTelemetry>(run => run.Status == "ok" && run.Requests == 2 && run.CacheHits == 0);
        second.Results.Should().BeEmpty();
        second.Indexers.Single().Status.Should().Be("trimmed");
        session.RequestsTo("site").Should().Be(2);
        SearchSession.SourceAnswered(second.Indexers.Single()).Should().BeTrue("a trimmed answer is complete, not a missing source");
    }

    [Fact]
    public async Task CacheHits_AreNotCounted_AndShowInTelemetry()
    {
        var session = new SearchSession();
        var (orchestrator, handler, _) = Create(session);

        await orchestrator.SearchAsync([Query("a"), Query("b")], [new FakeIndexer()], SearchLane.Background, SearchBudget.Background, CancellationToken.None);
        var again = await orchestrator.SearchAsync([Query("a"), Query("b")], [new FakeIndexer()], SearchLane.Background, SearchBudget.Background, CancellationToken.None);

        handler.Calls.Should().Be(2);
        again.Results.Should().HaveCount(2);
        again.Indexers.Single().Should().Match<IndexerRunTelemetry>(run => run.Status == "ok" && run.Requests == 0 && run.CacheHits == 2);
        again.Summary.Should().Contain("site:ok:2/").And.Contain("0req+2hit");
    }

    [Fact]
    public async Task InteractiveLane_GetsTwiceTheCap()
    {
        var session = new SearchSession();
        var (orchestrator, handler, _) = Create(session);

        var outcome = await orchestrator.SearchAsync([Query("a"), Query("b"), Query("c"), Query("d"), Query("e")], [new FakeIndexer()], SearchLane.Interactive, SearchBudget.Interactive, CancellationToken.None);

        handler.Calls.Should().Be(4);
        outcome.Results.Should().HaveCount(4);
        outcome.Indexers.Single().Status.Should().Be("trimmed");
    }

    [Fact]
    public async Task PagedIndexer_KeepsTheFirstPages_WhenTheCapStopsPaging()
    {
        var session = new SearchSession();
        var (orchestrator, handler, _) = Create(session);

        var outcome = await orchestrator.SearchAsync([Query("a")], [new FakeIndexer(pages: 4)], SearchLane.Background, SearchBudget.Background, CancellationToken.None);

        handler.Calls.Should().Be(2);
        outcome.Results.Should().HaveCount(2);
        outcome.Indexers.Single().Status.Should().Be("ok");
    }

    [Fact]
    public async Task LowBudget_SendsOnlyTheFirstRequestOfASearch()
    {
        var policy = Loose with { DailyBudget = 10, MaxRequestsPerSearch = 6 };
        var warmup = new SearchSession();
        var (orchestrator, handler, governor) = Create(warmup, policy);
        // Interactive budget 10: spend 9 so less than 20 % is left.
        for (var i = 0; i < 9; i++)
        {
            using var lease = await governor.AcquireAsync("site", SearchLane.Interactive, TimeSpan.Zero, CancellationToken.None);
        }

        governor.Remaining("site", SearchLane.Interactive).Should().Be(1);
        governor.RequestCapPerSearch("site", SearchLane.Interactive).Should().Be((1, true));

        var outcome = await orchestrator.SearchAsync([Query("a"), Query("a 1080p", specific: true)], [new FakeIndexer()], SearchLane.Interactive, SearchBudget.Interactive, CancellationToken.None);

        handler.Calls.Should().Be(1);
        outcome.Indexers.Single().Should().Match<IndexerRunTelemetry>(run => run.Status == "trimmed" && run.Detail!.Contains("budget low"));
    }

    [Fact]
    public void NoSource_IsReported_OnlyWhenNoSiteAnsweredAnySearch()
    {
        var session = new SearchSession();
        session.NoSourceAvailable.Should().BeFalse("no native search ran");

        session.Record([
            new IndexerRunTelemetry("knaben", "skipped", 0, 0, 0, "daily budget used (700/700, Background)"),
            new IndexerRunTelemetry("limetorrents", "skipped", 0, 0, 0, "breaker open until 10:20 UTC: blocked (HTTP 403)"),
            new IndexerRunTelemetry("yts", "skipped", 0, 0, 0, "disabled")
        ]);

        session.NoSourceAvailable.Should().BeTrue();
        session.NoSourceReason.Should().StartWith("No search source available:")
            .And.Contain("knaben skipped (daily budget used")
            .And.Contain("limetorrents skipped (breaker open")
            .And.NotContain("yts");

        session.Record([new IndexerRunTelemetry("nyaa", "ok", 3, 1, 100, null, 1)]);
        session.NoSourceAvailable.Should().BeFalse();
        session.NoSourceReason.Should().BeNull();
    }

    [Fact]
    public void NoSource_NamesThePauseSwitch()
    {
        SearchSession.DescribeNoSource([new IndexerRunTelemetry("knaben", "skipped", 0, 0, 0, "all searches paused (kill switch)")])
            .Should().Contain("paused");
        SearchSession.DescribeNoSource([new IndexerRunTelemetry("knaben", "skipped", 0, 0, 0, "disabled")])
            .Should().Contain("no native search site is enabled");
    }

    [Fact]
    public void PinBackend_KeepsTheFirstAnswer()
    {
        var session = new SearchSession();
        var answer = "Native";

        session.PinBackend("Movie|r1", () => answer).Should().Be("Native");
        answer = "qBittorrent";
        session.PinBackend("Movie|r1", () => answer).Should().Be("Native");
        session.PinBackend("Movie|r2", () => answer).Should().Be("qBittorrent");
    }
}
