using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class JellyfinClient : IJellyfinClient, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public JellyfinClient(ISettingsService settingsService, IAppLogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = RequireConfiguredSettings();
        using var request = CreateRequest(HttpMethod.Get, settings, "System/Info");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Jellyfin connection test failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimBody(body)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var serverName = document.RootElement.TryGetProperty("ServerName", out var nameElement)
            ? nameElement.GetString()
            : null;
        var version = document.RootElement.TryGetProperty("Version", out var versionElement)
            ? versionElement.GetString()
            : null;

        var summary = string.IsNullOrWhiteSpace(serverName)
            ? (version ?? "OK")
            : $"{serverName} ({version ?? "unknown"})";
        _logger.Info($"Connected to Jellyfin {summary}.", LogTarget.All);
        return summary;
    }

    public async Task ReportMediaUpdatedAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var settings = RequireConfiguredSettings();
        var updates = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new Dictionary<string, string>
            {
                ["Path"] = path,
                ["UpdateType"] = "Modified"
            })
            .ToList();

        if (updates.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new { Updates = updates });
        using var request = CreateRequest(HttpMethod.Post, settings, "Library/Media/Updated");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        _logger.Info(
            $"Jellyfin Library/Media/Updated for {updates.Count} path(s).",
            LogTarget.All);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Jellyfin Library/Media/Updated failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimBody(body)}");
        }

        _logger.Info(
            $"Jellyfin accepted path notify ({(int)response.StatusCode}) for {updates.Count} path(s).",
            LogTarget.All);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    private JellyfinRefreshSettings RequireConfiguredSettings()
    {
        var settings = _settingsService.Current.AutoTrack?.Jellyfin ?? new JellyfinRefreshSettings();
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("Jellyfin Base URL is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Jellyfin API key is not configured.");
        }

        return settings;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, JellyfinRefreshSettings settings, string relativePath)
    {
        var baseUrl = settings.BaseUrl.Trim().TrimEnd('/');
        var uri = new Uri($"{baseUrl}/{relativePath.TrimStart('/')}");
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{settings.ApiKey!.Trim()}\"");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static string TrimBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "...";
    }
}
