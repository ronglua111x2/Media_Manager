using System.Net;
using System.Text.Json;
using media_management_app.Models;

namespace SearchBench;

/// <summary>
/// Read-only qBittorrent search client (search/start, results, stop, delete). Keeps the WebUI session
/// cookie: search jobs belong to the session, and with localhost auth bypass no password is needed.
/// Reads the result buffer with a forward cursor until the job stops or the time budget ends.
/// </summary>
public sealed class QbittorrentSearchBackend : IDisposable
{
    private readonly HttpClient _http;

    public QbittorrentSearchBackend(string baseUrl = "http://localhost:8080/api/v2/")
    {
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
        _http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task InitAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("app/version", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<SearchRun> SearchAsync(string query, string plugins, int maxSeconds, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        int? id = null;
        var rows = new List<TorrentSearchResult>();
        var status = "Running";
        try
        {
            for (var attempt = 0; attempt < 6 && id is null; attempt++)
            {
                using var start = await _http.PostAsync("search/start", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["pattern"] = query,
                    ["plugins"] = plugins,
                    ["category"] = "all"
                }), ct);
                if (start.StatusCode == HttpStatusCode.Conflict)
                {
                    await Task.Delay(3000, ct);
                    continue;
                }

                start.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await start.Content.ReadAsStringAsync(ct));
                id = doc.RootElement.GetProperty("id").GetInt32();
            }

            if (id is null)
            {
                return new SearchRun(query, [], "NoSlot", 0);
            }

            while (true)
            {
                using var response = await _http.GetAsync($"search/results?id={id}&limit=1000&offset={rows.Count}", ct);
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                status = root.GetProperty("status").GetString() ?? "Unknown";
                var total = root.TryGetProperty("total", out var totalElement) ? totalElement.GetInt32() : 0;
                foreach (var element in root.GetProperty("results").EnumerateArray())
                {
                    var fileUrl = Str(element, "fileUrl");
                    var descr = Str(element, "descrLink");
                    rows.Add(new TorrentSearchResult
                    {
                        FileName = Str(element, "fileName"),
                        FileSize = element.TryGetProperty("fileSize", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
                        FileUrl = string.IsNullOrWhiteSpace(fileUrl) ? descr : fileUrl,
                        DescriptionUrl = descr,
                        Seeders = element.TryGetProperty("nbSeeders", out var s) && s.TryGetInt32(out var seeds) ? seeds : 0,
                        Leechers = element.TryGetProperty("nbLeechers", out var l) && l.TryGetInt32(out var leech) ? leech : 0,
                        EngineName = Str(element, "engineName"),
                        SiteUrl = Str(element, "siteUrl")
                    });
                }

                if (status != "Running" && rows.Count >= total)
                {
                    break;
                }

                if ((DateTimeOffset.UtcNow - started).TotalSeconds >= maxSeconds)
                {
                    break;
                }

                await Task.Delay(1000, ct);
            }
        }
        finally
        {
            if (id is not null)
            {
                try
                {
                    if (status == "Running")
                    {
                        (await _http.PostAsync("search/stop", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = id.Value.ToString() }), CancellationToken.None)).Dispose();
                    }

                    (await _http.PostAsync("search/delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = id.Value.ToString() }), CancellationToken.None)).Dispose();
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }

        return new SearchRun(query, rows, status, (DateTimeOffset.UtcNow - started).TotalSeconds);
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    public void Dispose() => _http.Dispose();
}

public sealed record SearchRun(string Query, IReadOnlyList<TorrentSearchResult> Rows, string Status, double Seconds);
