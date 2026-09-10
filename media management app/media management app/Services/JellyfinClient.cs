using System.Globalization;
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

    public async Task<IReadOnlyList<JellyfinScheduledTaskInfo>> GetScheduledTasksAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = RequireConfiguredSettings();
        using var request = CreateRequest(HttpMethod.Get, settings, "ScheduledTasks");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Jellyfin ScheduledTasks failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimBody(body)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tasks = new List<JellyfinScheduledTaskInfo>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            tasks.Add(new JellyfinScheduledTaskInfo
            {
                Id = TryGetString(element, "Id"),
                Key = TryGetString(element, "Key"),
                Name = TryGetString(element, "Name"),
                Category = TryGetString(element, "Category"),
                State = TryGetString(element, "State")
            });
        }

        return tasks;
    }

    public Task<string?> FindSeriesItemIdAsync(
        int showTmdbId,
        CancellationToken cancellationToken = default)
    {
        var settings = RequireConfiguredSettings();
        return FindItemIdByTmdbAsync(settings, "Series", showTmdbId, cancellationToken);
    }

    public Task<string?> FindMovieItemIdAsync(
        int movieTmdbId,
        CancellationToken cancellationToken = default)
    {
        var settings = RequireConfiguredSettings();
        return FindItemIdByTmdbAsync(settings, "Movie", movieTmdbId, cancellationToken);
    }

    public async Task<string?> FindEpisodeItemIdAsync(
        int showTmdbId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken = default)
    {
        var settings = RequireConfiguredSettings();
        var seriesId = await FindItemIdByTmdbAsync(settings, "Series", showTmdbId, cancellationToken);
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            return null;
        }

        return await FindEpisodeIdAsync(settings, seriesId, seasonNumber, episodeNumber, cancellationToken);
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

    private async Task<string?> FindItemIdByTmdbAsync(
        JellyfinRefreshSettings settings,
        string includeItemType,
        int tmdbId,
        CancellationToken cancellationToken)
    {
        var tmdb = tmdbId.ToString(CultureInfo.InvariantCulture);
        var providerQuery = Uri.EscapeDataString($"Tmdb.{tmdb}");
        var targetedPath =
            $"Items?Recursive=true&IncludeItemTypes={includeItemType}&AnyProviderIdEquals={providerQuery}&Fields=ProviderIds&EnableImages=false&EnableUserData=false&Limit=1";
        var targetedId = await FindMatchingItemIdAsync(settings, targetedPath, tmdb, cancellationToken);
        if (!string.IsNullOrWhiteSpace(targetedId))
        {
            return targetedId;
        }

        var fallbackPath =
            $"Items?Recursive=true&IncludeItemTypes={includeItemType}&Fields=ProviderIds&EnableImages=false&EnableUserData=false&HasTmdbId=true";
        return await FindMatchingItemIdAsync(settings, fallbackPath, tmdb, cancellationToken);
    }

    private async Task<string?> FindMatchingItemIdAsync(
        JellyfinRefreshSettings settings,
        string relativePath,
        string tmdbId,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(settings, relativePath, cancellationToken);
        foreach (var item in EnumerateItems(document.RootElement))
        {
            if (!ProviderIdsMatch(item, "Tmdb", tmdbId))
            {
                continue;
            }

            var id = TryGetString(item, "Id") ?? TryGetString(item, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }

        return null;
    }

    private async Task<string?> FindEpisodeIdAsync(
        JellyfinRefreshSettings settings,
        string seriesId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken)
    {
        var encodedSeriesId = Uri.EscapeDataString(seriesId);
        var path =
            $"Shows/{encodedSeriesId}/Episodes?season={seasonNumber.ToString(CultureInfo.InvariantCulture)}&Fields=IndexNumber,ParentIndexNumber&EnableImages=false&EnableUserData=false";
        using var document = await GetJsonAsync(settings, path, cancellationToken);
        foreach (var item in EnumerateItems(document.RootElement))
        {
            var indexNumber = TryGetInt32(item, "IndexNumber") ?? TryGetInt32(item, "indexNumber");
            if (indexNumber != episodeNumber)
            {
                continue;
            }

            var parentIndex = TryGetInt32(item, "ParentIndexNumber") ?? TryGetInt32(item, "parentIndexNumber");
            if (parentIndex is not null && parentIndex != seasonNumber)
            {
                continue;
            }

            var id = TryGetString(item, "Id") ?? TryGetString(item, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }

        return null;
    }

    private async Task<JsonDocument> GetJsonAsync(
        JellyfinRefreshSettings settings,
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, settings, relativePath);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Jellyfin request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimBody(body)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static IEnumerable<JsonElement> EnumerateItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                yield return element;
            }

            yield break;
        }

        if (TryGetPropertyIgnoreCase(root, "Items", out var items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in items.EnumerateArray())
            {
                yield return element;
            }
        }
    }

    private static bool ProviderIdsMatch(JsonElement item, string providerName, string expectedId)
    {
        if (!TryGetPropertyIgnoreCase(item, "ProviderIds", out var ids) ||
            ids.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in ids.EnumerateObject())
        {
            if (!property.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ToString();
            return string.Equals(value, expectedId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.GetString();
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
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
