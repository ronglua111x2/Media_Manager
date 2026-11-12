using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using MediaManager.App.Tests.Cleanup;
using media_management_app.Models;
using media_management_app.Services.Gemini;
using media_management_app.Services.Gemini.Routing;

namespace MediaManager.App.Tests.Gemini;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    public ManualTimeProvider(DateTimeOffset start) => UtcNow = start;

    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>One HTTP request the fake handler saw.</summary>
internal sealed record RecordedRequest(string Method, string Url, string? ApiKeyHeader, string Body);

/// <summary>HTTP handler that answers from a queue (or a function) and records every request.</summary>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, HttpResponseMessage> _respond;

    public ScriptedHandler(Func<RecordedRequest, HttpResponseMessage> respond) => _respond = respond;

    public List<RecordedRequest> Requests { get; } = [];

    public int Calls => Requests.Count;

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Success(string text = "{\"m\":[[0,1]],\"w\":[]}") =>
        Json(HttpStatusCode.OK, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" +
            System.Text.Json.JsonSerializer.Serialize(text) + "}]},\"finishReason\":\"STOP\"}]}");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var key = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.FirstOrDefault() : null;
        var recorded = new RecordedRequest(request.Method.Method, request.RequestUri!.ToString(), key, body);
        Requests.Add(recorded);
        return _respond(recorded);
    }
}

/// <summary>Gemini client + router + store + real catalog over a temporary state folder and a manual clock.</summary>
internal sealed class GeminiTestHost : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 11, 12, 14, 0, 0, TimeSpan.Zero);

    private readonly string _stateFolder = Path.Combine(Path.GetTempPath(), $"mm-gemini-router-{Guid.NewGuid():N}");

    public GeminiTestHost(Func<RecordedRequest, HttpResponseMessage> respond, Action<GeminiSettings>? configure = null, Action<string>? beforeCatalog = null)
    {
        Directory.CreateDirectory(_stateFolder);
        Settings.Current.StateFolder = _stateFolder;
        Settings.Current.Gemini = new GeminiSettings
        {
            Enabled = true,
            ApiKey = "test-key"
        };
        configure?.Invoke(Settings.Current.Gemini);
        beforeCatalog?.Invoke(_stateFolder);

        Handler = new ScriptedHandler(respond);
        var httpClient = new HttpClient(Handler);
        Catalog = new GeminiModelCatalogService(Settings, httpClient, Logger);
        Store = new GeminiModelAvailabilityStore(Settings, Clock);
        Router = new GeminiModelRouter(Settings, Catalog, Store, Logger);
        Client = new GeminiApiClient(Settings, httpClient, Router, Logger);
    }

    public FakeSettingsService Settings { get; } = new();

    public FakeAppLogger Logger { get; } = new();

    public ManualTimeProvider Clock { get; } = new(Start);

    public ScriptedHandler Handler { get; }

    public GeminiModelCatalogService Catalog { get; }

    public GeminiModelAvailabilityStore Store { get; }

    public GeminiModelRouter Router { get; }

    public GeminiApiClient Client { get; }

    public string StateFolder => _stateFolder;

    public GeminiModelSnapshot Snapshot(string model) =>
        Store.GetSnapshot(model, Catalog.Current.Models.FirstOrDefault(entry => entry.Id == model)?.FreeRequestsPerDay);

    public static string ModelOf(RecordedRequest request)
    {
        var start = request.Url.IndexOf("/models/", StringComparison.Ordinal) + "/models/".Length;
        var end = request.Url.IndexOf(':', start);
        return request.Url[start..end];
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
}
