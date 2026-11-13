using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using MediaManager.App.Tests.Cleanup;
using media_management_app.Models;
using media_management_app.Services.Gemini;
using media_management_app.Services.Gemini.Routing;

namespace MediaManager.App.Tests.Special;

/// <summary>
/// The Gemini client with a fake HTTP handler (no network): cancelling must stop the call instead of moving on
/// to the next model (SPEC-08), and a response cut short must say so (note R5).
/// </summary>
public sealed class GeminiClientCancellationTests : IDisposable
{
    private readonly string _stateFolder = Path.Combine(Path.GetTempPath(), $"mm-gemini-{Guid.NewGuid():N}");
    private readonly FakeSettingsService _settings = new();

    public GeminiClientCancellationTests()
    {
        Directory.CreateDirectory(_stateFolder);
        _settings.Current.StateFolder = _stateFolder;
        _settings.Current.Gemini = new GeminiSettings
        {
            Enabled = true,
            ApiKey = "test-key",
            AutoSelectModel = false,
            Model = "gemini-test-primary",
            FallbackModels = ["gemini-test-fallback"]
        };
    }

    [Fact]
    public async Task GenerateJson_CancelledWhileWaiting_ThrowsCancellationWithoutTryingFallbackModels()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GenerateJsonTextWithModelAsync("system", "{}", cancellationToken: cancellation.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GenerateJson_ResponseCutByTokenLimit_ReportsFinishReason()
    {
        const string body =
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"m\\\":[[0,1]],\\\"w\\\":[]}\"}]},\"finishReason\":\"MAX_TOKENS\"}]}";
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        var client = CreateClient(handler);

        var result = await client.GenerateJsonTextWithModelAsync("system", "{}");

        Assert.Equal("MAX_TOKENS", result.FinishReason);
        Assert.Equal("{\"m\":[[0,1]],\"w\":[]}", result.Json);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_stateFolder, recursive: true);
        }
        catch
        {
            // Temporary folder cleanup is best effort.
        }
    }

    private GeminiApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var logger = new FakeAppLogger();
        var catalog = new GeminiModelCatalogService(_settings, httpClient, logger);
        var router = new GeminiModelRouter(_settings, catalog, new GeminiModelAvailabilityStore(_settings, TimeProvider.System), logger);
        return new GeminiApiClient(_settings, httpClient, router, logger);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return _send(request, cancellationToken);
        }
    }
}
