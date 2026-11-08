using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using media_management_app.Services;
using MediaManager.App.Tests.Cleanup;

namespace MediaManager.App.Tests.Services;

public class TmdbMetadataProviderRequestTests
{
    private const string Token = "test-tmdb-token";

    private static (TmdbMetadataProvider Provider, HttpClient Client, RecordingHandler Handler) Create()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler);
        var settings = new FakeSettingsService();
        settings.Current.TmdbReadAccessToken = Token;
        return (new TmdbMetadataProvider(settings, new FakeAppLogger(), client, new NoOpBlockAdvisor()), client, handler);
    }

    [Fact]
    public async Task TmdbCall_PutsCredentialsOnTheRequest_NotOnTheSharedClient()
    {
        var (provider, client, handler) = Create();

        await provider.SearchTvShowsLightweightAsync("Dark Item");

        var sent = Assert.Single(handler.Sent);
        Assert.StartsWith("https://api.themoviedb.org/3/search/tv?query=Dark%20Item", sent.Uri.AbsoluteUri);
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

    private sealed record SentRequest(Uri Uri, string? Authorization, string? Accept);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public ConcurrentBag<SentRequest> Sent { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add(new SentRequest(
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.FirstOrDefault()?.ToString()));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"results":[]}""", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class NoOpBlockAdvisor : ITmdbBlockAdvisor
    {
        public void Warn(Exception exception, CancellationToken cancellationToken = default)
        {
        }
    }
}
