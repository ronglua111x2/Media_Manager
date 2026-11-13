using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using media_management_app.Common;
using media_management_app.Services;
using MediaManager.App.Tests.Cleanup;

namespace MediaManager.App.Tests.Services;

public class TmdbMetadataProviderRequestTests
{
    private const string Token = "test-tmdb-token";

    private static (TmdbMetadataProvider Provider, HttpClient Client, RecordingHandler Handler) Create(string? apiHost = null)
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler);
        var settings = new FakeSettingsService();
        settings.Current.TmdbReadAccessToken = Token;
        settings.Current.TmdbApiHost = apiHost;
        return (new TmdbMetadataProvider(settings, new FakeAppLogger(), client, new NoOpBlockAdvisor()), client, handler);
    }

    [Fact]
    public async Task TmdbCall_PutsCredentialsOnTheRequest_NotOnTheSharedClient()
    {
        var (provider, client, handler) = Create();

        await provider.SearchTvShowsLightweightAsync("Dark Item");

        var sent = Assert.Single(handler.Sent);
        Assert.StartsWith("https://api.tmdb.org/3/search/tv?query=Dark%20Item", sent.Uri.AbsoluteUri);
        Assert.Equal("Bearer " + Token, sent.Authorization);
        Assert.Equal("application/json", sent.Accept);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.Empty(client.DefaultRequestHeaders.Accept);
        Assert.Null(client.BaseAddress);
    }

    [Fact]
    public async Task AfterTmdbCall_OtherHostsOnTheSameClient_GetNoTmdbToken()
    {
        var (provider, client, handler) = Create();

        await provider.SearchTvShowsLightweightAsync("Dark Item");
        using var response = await client.GetAsync("https://generativelanguage.googleapis.com/v1beta/models?key=x");

        var gemini = Assert.Single(handler.Sent, item => item.Uri.Host == "generativelanguage.googleapis.com");
        Assert.Null(gemini.Authorization);
    }

    [Fact]
    public async Task ConcurrentTmdbCalls_AllCarryTheToken()
    {
        var (provider, _, handler) = Create();

        await Task.WhenAll(Enumerable.Range(0, 64).Select(index =>
            Task.Run(() => provider.SearchTvShowsLightweightAsync($"Show {index}"))));

        Assert.Equal(64, handler.Sent.Count);
        Assert.All(handler.Sent, item => Assert.Equal("Bearer " + Token, item.Authorization));
    }

    [Theory]
    [InlineData("api.example.org", "api.example.org")]
    [InlineData("not a host", "api.tmdb.org")]
    [InlineData("https://api.example.org/3", "api.tmdb.org")]
    public async Task ConfiguredHost_IsUsed_InvalidFallsBackToDefault(string configured, string expectedHost)
    {
        var (provider, _, handler) = Create(configured);

        await provider.SearchTvShowsLightweightAsync("Dark Item");

        Assert.Equal(expectedHost, Assert.Single(handler.Sent).Uri.Host);
    }

    [Fact]
    public async Task WarpRetry_UsesTheOriginalHost_ThroughTheTunnel()
    {
        var (provider, _, handler) = Create();
        var refused = new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        await TmdbWarpRecovery.RetryWithWarpAsync(
            "test",
            refused,
            ct => provider.SearchTvShowsLightweightAsync("Dark Item", ct),
            _ => Task.FromResult(new WarpLeaseResult(true, true, true)),
            new FakeAppLogger(),
            CancellationToken.None,
            _ => Task.FromResult<(bool, IReadOnlyCollection<System.Net.IPAddress>)>((true, [])),
            (_, _) => Task.CompletedTask);

        Assert.Equal(NetworkEndpoints.TmdbLegacyApiHost, Assert.Single(handler.Sent).Uri.Host);
    }

    [Fact]
    public async Task Redirect_IsClassifiedAsHostMoved()
    {
        var (provider, _, handler) = Create();
        handler.Respond = () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            response.Headers.Location = new Uri("https://www.themoviedb.org/documentation/api");
            return response;
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchTvShowsLightweightAsync("Dark Item"));

        Assert.Equal(NetFailure.HostMoved, NetworkFailureClassifier.Classify(ex));
    }

    [Fact]
    public async Task HtmlPage_IsClassifiedAsBadResponse()
    {
        var (provider, _, handler) = Create();
        handler.Respond = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>blocked</html>", Encoding.UTF8, "text/html")
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchTvShowsLightweightAsync("Dark Item"));

        Assert.Equal(NetFailure.BadResponse, NetworkFailureClassifier.Classify(ex));
    }

    [Fact]
    public async Task HttpClientTimeout_SurfacesAsTimeout_NotAsCancellation()
    {
        var (provider, _, handler) = Create();
        handler.Respond = () => throw new TaskCanceledException("timeout", new TimeoutException());

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => provider.SearchTvShowsLightweightAsync("Dark Item"));

        Assert.Equal(NetFailure.Timeout, NetworkFailureClassifier.Classify(ex));
    }

    // Merge of PR #12 with native search (main): the IMDb id lookup native search runs from its own threads goes
    // through the same per-request path, so it gets the api.tmdb.org host and the token without touching the client.
    [Fact]
    public async Task NativeSearchImdbLookup_UsesTheApiHost_AndPerRequestToken()
    {
        var (provider, client, handler) = Create();
        handler.Respond = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"imdb_id":"tt0000001"}""", Encoding.UTF8, "application/json")
        };

        var imdbId = await provider.GetImdbIdAsync(isMovie: false, 42);

        Assert.Equal("tt0000001", imdbId);
        var sent = Assert.Single(handler.Sent);
        Assert.Equal("https://api.tmdb.org/3/tv/42/external_ids", sent.Uri.AbsoluteUri);
        Assert.Equal("Bearer " + Token, sent.Authorization);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task ImdbLookupsAndShowSearches_RunningTogether_AllCarryTheToken()
    {
        var (provider, _, handler) = Create();

        await Task.WhenAll(Enumerable.Range(0, 32).SelectMany(index => new Task[]
        {
            Task.Run(() => provider.SearchTvShowsLightweightAsync($"Show {index}")),
            Task.Run(() => provider.GetImdbIdAsync(index % 2 == 0, index))
        }));

        Assert.Equal(64, handler.Sent.Count);
        Assert.All(handler.Sent, item =>
        {
            Assert.Equal("Bearer " + Token, item.Authorization);
            Assert.Equal("api.tmdb.org", item.Uri.Host);
        });
    }

    [Fact]
    public async Task NativeSearchLookupDeadline_StaysACancellation_AndDoesNotWarnAboutWarp()
    {
        // Native search gives the lookup a linked token with its own deadline (07c NS-03) and treats an
        // OperationCanceledException as "lookup timed out". PR #12 turns HttpClient timeouts into TimeoutException;
        // that must not apply when the caller's own token fired, and the WARP hint must stay quiet.
        var handler = new DelayHandler();
        var settings = new FakeSettingsService();
        settings.Current.TmdbReadAccessToken = Token;
        var advisor = new CountingBlockAdvisor();
        var provider = new TmdbMetadataProvider(settings, new FakeAppLogger(), new HttpClient(handler), advisor);
        using var lookup = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetImdbIdAsync(false, 42, lookup.Token));

        Assert.Equal(0, advisor.Count);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TmdbClientTimeout_DuringImdbLookup_IsNotMistakenForTheCallersCancel()
    {
        // A TMDB HttpClient timeout (caller token not cancelled) surfaces as TimeoutException, which native search
        // logs as a failed lookup (retried sooner) rather than as the user pressing Cancel.
        var (provider, _, handler) = Create();
        handler.Respond = () => throw new TaskCanceledException("timeout", new TimeoutException());

        await Assert.ThrowsAsync<TimeoutException>(() => provider.GetImdbIdAsync(false, 42, CancellationToken.None));
    }

    private sealed class DelayHandler : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class CountingBlockAdvisor : ITmdbBlockAdvisor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Warn(Exception exception, CancellationToken cancellationToken = default) => Interlocked.Increment(ref _count);
    }

    private sealed record SentRequest(Uri Uri, string? Authorization, string? Accept);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public ConcurrentBag<SentRequest> Sent { get; } = [];

        public Func<HttpResponseMessage> Respond { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"results":[]}""", Encoding.UTF8, "application/json")
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add(new SentRequest(
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.FirstOrDefault()?.ToString()));
            return Task.FromResult(Respond());
        }
    }

    private sealed class NoOpBlockAdvisor : ITmdbBlockAdvisor
    {
        public void Warn(Exception exception, CancellationToken cancellationToken = default)
        {
        }
    }
}
