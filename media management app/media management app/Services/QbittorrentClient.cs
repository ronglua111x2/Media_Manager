using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class QbittorrentClient : IQbittorrentClient, IDisposable
{
    private const int SearchPollDelayMilliseconds = 1000;
    private const int SearchTimeoutSeconds = 30;
    private const int AddVerifyTimeoutSeconds = 20;
    private const int MaxResolverBytes = 2 * 1024 * 1024;

    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _httpClient;

    public QbittorrentClient(ISettingsService settingsService, IAppLogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true
        });
    }

    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await LoginAsync(cancellationToken);
        using var response = await _httpClient.GetAsync(CreateUri("api/v2/app/version"), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"qBittorrent connection test failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var version = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        _logger.Info($"Connected to qBittorrent Web UI {version}.", LogTarget.All);
        return version;
    }

    public async Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(TorrentSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new InvalidOperationException("Search query is empty.");
        }

        await LoginAsync(cancellationToken);
        var searchId = await StartSearchAsync(request, cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(SearchTimeoutSeconds);
        IReadOnlyList<TorrentSearchResult> latestResults = [];
        var latestStatus = "Running";

        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await GetSearchResultsAsync(searchId, request.Limit, cancellationToken);
            latestResults = response.Results;
            latestStatus = response.Status;

            if (!string.Equals(latestStatus, "Running", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            await Task.Delay(SearchPollDelayMilliseconds, cancellationToken);
        }

        _logger.Info(
            $"qBittorrent search completed. Query='{request.Query}', Status='{latestStatus}', Results={latestResults.Count}.",
            LogTarget.All);

        return latestResults
            .OrderByDescending(result => result.Seeders)
            .ThenBy(result => result.FileSize)
            .ToList();
    }

    public async Task<AddedTorrentResult> AddTorrentAsync(AddTorrentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            throw new InvalidOperationException("Torrent URL is empty.");
        }
        if (!IsSupportedTorrentUrl(request.Url))
        {
            throw new InvalidOperationException("This search result is not a supported qBittorrent URL.");
        }
        var savePath = string.IsNullOrWhiteSpace(request.SavePath) ? null : request.SavePath.Trim();
        if (!string.IsNullOrWhiteSpace(savePath) && !Directory.Exists(savePath))
        {
            throw new InvalidOperationException($"Auto Torrent download folder does not exist: {savePath}");
        }

        await LoginAsync(cancellationToken);
        var category = await TryPrepareCategoryAsync(request.Category, savePath, cancellationToken);

        var existingHashes = (await GetTorrentsAsync(cancellationToken))
            .Select(torrent => torrent.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.PluginName))
        {
            _logger.Info($"Trying qBittorrent search plugin download. Plugin='{request.PluginName}', Url='{request.Url}'", LogTarget.All);
            await PostSearchDownloadTorrentAsync(request.Url, request.PluginName, cancellationToken);
            var pluginAddedTorrent = await WaitForAddedTorrentAsync(existingHashes, null, cancellationToken);
            if (pluginAddedTorrent is not null)
            {
                await ApplyTorrentPostAddSettingsAsync(pluginAddedTorrent.Hash, savePath, category, request.Tags, cancellationToken);
                pluginAddedTorrent = await GetTorrentAsync(pluginAddedTorrent.Hash, cancellationToken) ?? pluginAddedTorrent;
                _logger.Info(
                    $"Verified torrent from search plugin. Name='{pluginAddedTorrent.Name}', Hash={pluginAddedTorrent.Hash}, State={pluginAddedTorrent.State}, SavePath='{pluginAddedTorrent.SavePath}', Category='{pluginAddedTorrent.Category}'.",
                    LogTarget.All);

                return pluginAddedTorrent;
            }

            _logger.Warning("qBittorrent search plugin download did not create a torrent. Falling back to URL resolver.", LogTarget.All);
        }

        var addSources = await ResolveAddSourcesAsync(request.Url, cancellationToken);
        foreach (var source in addSources)
        {
            _logger.Info($"Trying qBittorrent add source: {source.Description}", LogTarget.All);
            await PostAddTorrentAsync(source, request, savePath, category, cancellationToken);

            var addedTorrent = await WaitForAddedTorrentAsync(existingHashes, category, cancellationToken);
            if (addedTorrent is not null)
            {
                _logger.Info(
                    $"Verified torrent in qBittorrent. Name='{addedTorrent.Name}', Hash={addedTorrent.Hash}, State={addedTorrent.State}, SavePath='{addedTorrent.SavePath}', Category='{addedTorrent.Category}'.",
                    LogTarget.All);

                return addedTorrent;
            }

            _logger.Warning($"qBittorrent accepted source but no new torrent appeared: {source.Description}", LogTarget.All);
        }

        throw new InvalidOperationException("qBittorrent accepted the add request, but no new torrent appeared. The search plugin URL may be a details page, blocked URL, or dead torrent link.");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        _cookies.GetCookies(GetBaseUri()).Clear();

        var settings = _settingsService.Current.AutoTorrent;
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = settings.Username ?? string.Empty,
            ["password"] = settings.Password ?? string.Empty
        });

        using var response = await _httpClient.PostAsync(CreateUri("api/v2/auth/login"), content, cancellationToken);
        var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (!response.IsSuccessStatusCode || !body.Equals("Ok.", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("qBittorrent login failed. Check Web UI URL, username, password, and Web UI settings.");
        }
    }

    private async Task<string?> TryPrepareCategoryAsync(string categoryName, string? savePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return null;
        }

        categoryName = categoryName.Trim();
        var categories = await GetCategoriesAsync(cancellationToken);
        if (categories.ContainsKey(categoryName))
        {
            return categoryName;
        }

        if (string.IsNullOrWhiteSpace(savePath))
        {
            _logger.Warning($"qBittorrent category '{categoryName}' does not exist and no save path is configured. Adding torrent without category.", LogTarget.All);
            return null;
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["category"] = categoryName,
            ["savePath"] = savePath
        });

        using var response = await _httpClient.PostAsync(CreateUri("api/v2/torrents/createCategory"), content, cancellationToken);
        var responseText = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (!response.IsSuccessStatusCode || responseText.Equals("Fails.", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning(
                $"Failed to create qBittorrent category '{categoryName}'. Adding torrent without category. Response: {(int)response.StatusCode} {response.ReasonPhrase} {responseText}".Trim(),
                LogTarget.All);
            return null;
        }

        return categoryName;
    }

    private async Task<Dictionary<string, string?>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(CreateUri("api/v2/torrents/categories"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var categories = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return categories;
        }

        foreach (var categoryProperty in document.RootElement.EnumerateObject())
        {
            var savePath = categoryProperty.Value.ValueKind == JsonValueKind.Object
                ? GetString(categoryProperty.Value, "savePath")
                : null;
            categories[categoryProperty.Name] = savePath;
        }

        return categories;
    }

    private async Task<AddedTorrentResult?> WaitForAddedTorrentAsync(HashSet<string> existingHashes, string? categoryName, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(AddVerifyTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var addedTorrent = (await GetTorrentsAsync(cancellationToken))
                .FirstOrDefault(torrent =>
                    !existingHashes.Contains(torrent.Hash) &&
                    (string.IsNullOrWhiteSpace(categoryName) ||
                     string.Equals(torrent.Category, categoryName, StringComparison.OrdinalIgnoreCase)));

            if (addedTorrent is not null)
            {
                return addedTorrent;
            }

            await Task.Delay(SearchPollDelayMilliseconds, cancellationToken);
        }

        return null;
    }

    private async Task<IReadOnlyList<AddedTorrentResult>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(CreateUri("api/v2/torrents/info"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var torrents = new List<AddedTorrentResult>();

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return torrents;
        }

        foreach (var torrentElement in document.RootElement.EnumerateArray())
        {
            var hash = GetString(torrentElement, "hash") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(hash))
            {
                continue;
            }

            torrents.Add(new AddedTorrentResult
            {
                Hash = hash,
                Name = GetString(torrentElement, "name") ?? string.Empty,
                State = GetString(torrentElement, "state") ?? string.Empty,
                SavePath = GetString(torrentElement, "save_path") ?? string.Empty,
                Category = GetString(torrentElement, "category") ?? string.Empty
            });
        }

        return torrents;
    }

    private async Task<AddedTorrentResult?> GetTorrentAsync(string hash, CancellationToken cancellationToken)
    {
        return (await GetTorrentsAsync(cancellationToken))
            .FirstOrDefault(torrent => string.Equals(torrent.Hash, hash, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<int> StartSearchAsync(TorrentSearchRequest request, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["pattern"] = request.Query,
            ["plugins"] = request.Plugins,
            ["category"] = request.Category
        });

        using var response = await _httpClient.PostAsync(CreateUri("api/v2/search/start"), content, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id))
        {
            throw new InvalidOperationException("qBittorrent did not return a valid search id.");
        }

        return id;
    }

    private async Task<SearchResultsResponse> GetSearchResultsAsync(int searchId, int limit, CancellationToken cancellationToken)
    {
        var path = $"api/v2/search/results?id={searchId}&limit={Math.Max(limit, 1)}&offset=0";
        using var response = await _httpClient.GetAsync(CreateUri(path), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var status = GetString(root, "status") ?? "Unknown";
        var results = new List<TorrentSearchResult>();

        if (root.TryGetProperty("results", out var resultsElement) && resultsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var resultElement in resultsElement.EnumerateArray())
            {
                var fileUrl = GetString(resultElement, "fileUrl") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(fileUrl))
                {
                    continue;
                }

                results.Add(new TorrentSearchResult
                {
                    FileName = GetString(resultElement, "fileName") ?? string.Empty,
                    FileSize = GetLong(resultElement, "fileSize"),
                    FileUrl = fileUrl,
                    Seeders = GetInt(resultElement, "nbSeeders"),
                    Leechers = GetInt(resultElement, "nbLeechers"),
                    EngineName = GetString(resultElement, "engineName") ?? string.Empty,
                    SiteUrl = GetString(resultElement, "siteUrl") ?? string.Empty
                });
            }
        }

        return new SearchResultsResponse(status, results);
    }

    private async Task<IReadOnlyList<TorrentAddSource>> ResolveAddSourcesAsync(string url, CancellationToken cancellationToken)
    {
        if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("bc://bt/", StringComparison.OrdinalIgnoreCase))
        {
            return [TorrentAddSource.FromUrl(url, "original magnet/bt URL")];
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var sourceUri) || sourceUri.Scheme is not ("http" or "https"))
        {
            return [TorrentAddSource.FromUrl(url, "original URL")];
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
            request.Headers.UserAgent.ParseAdd("MediaManager/1.0");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning($"Could not resolve torrent URL before add. Response: {(int)response.StatusCode} {response.ReasonPhrase}. URL='{url}'", LogTarget.All);
                return [TorrentAddSource.FromUrl(url, "original URL")];
            }

            var bytes = await ReadLimitedBytesAsync(response.Content, MaxResolverBytes, cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (IsTorrentPayload(contentType, bytes))
            {
                return [TorrentAddSource.FromTorrentBytes(bytes, GetTorrentFileName(sourceUri), "downloaded .torrent payload")];
            }

            var html = DecodeText(bytes, response.Content.Headers.ContentType);
            var resolvedSources = ExtractTorrentSources(html, sourceUri);
            if (resolvedSources.Count > 0)
            {
                return resolvedSources;
            }

            _logger.Warning($"No magnet or .torrent link was found in resolved HTML. URL='{url}'", LogTarget.All);
            return [TorrentAddSource.FromUrl(url, "original URL")];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.Warning($"Failed to resolve torrent URL before add. Falling back to original URL. URL='{url}', Error='{ex.Message}'", LogTarget.All);
            return [TorrentAddSource.FromUrl(url, "original URL")];
        }
    }

    private async Task PostAddTorrentAsync(
        TorrentAddSource source,
        AddTorrentRequest request,
        string? savePath,
        string? category,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        if (source.TorrentBytes is not null)
        {
            var torrentContent = new ByteArrayContent(source.TorrentBytes);
            torrentContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            content.Add(torrentContent, "torrents", source.FileName);
        }
        else if (!string.IsNullOrWhiteSpace(source.Url))
        {
            AddStringContent(content, "urls", source.Url);
        }
        else
        {
            throw new InvalidOperationException("Resolved torrent source is empty.");
        }

        if (!string.IsNullOrWhiteSpace(savePath))
        {
            AddStringContent(content, "savepath", savePath);
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            AddStringContent(content, "category", category);
        }
        AddStringContent(content, "tags", request.Tags);
        AddStringContent(content, "paused", request.Paused ? "true" : "false");

        using var response = await _httpClient.PostAsync(CreateUri("api/v2/torrents/add"), content, cancellationToken);
        var responseText = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (!response.IsSuccessStatusCode || responseText.Equals("Fails.", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Failed to add torrent: {(int)response.StatusCode} {response.ReasonPhrase} {responseText}".Trim());
        }
    }

    private async Task PostSearchDownloadTorrentAsync(string torrentUrl, string pluginName, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["torrentUrl"] = torrentUrl,
            ["pluginName"] = pluginName
        });

        using var response = await _httpClient.PostAsync(CreateUri("api/v2/search/downloadTorrent"), content, cancellationToken);
        var responseText = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (!response.IsSuccessStatusCode || responseText.Equals("Fails.", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Failed to download torrent through search plugin: {(int)response.StatusCode} {response.ReasonPhrase} {responseText}".Trim());
        }
    }

    private async Task ApplyTorrentPostAddSettingsAsync(string hash, string? savePath, string? category, string tags, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(savePath))
        {
            await PostFormAsync("api/v2/torrents/setLocation", new Dictionary<string, string>
            {
                ["hashes"] = hash,
                ["location"] = savePath
            }, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            await PostFormAsync("api/v2/torrents/setCategory", new Dictionary<string, string>
            {
                ["hashes"] = hash,
                ["category"] = category
            }, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(tags))
        {
            await PostFormAsync("api/v2/torrents/addTags", new Dictionary<string, string>
            {
                ["hashes"] = hash,
                ["tags"] = tags
            }, cancellationToken);
        }
    }

    private async Task PostFormAsync(string relativePath, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(CreateUri(relativePath), content, cancellationToken);
        var responseText = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (!response.IsSuccessStatusCode || responseText.Equals("Fails.", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning($"qBittorrent post-add operation failed for '{relativePath}': {(int)response.StatusCode} {response.ReasonPhrase} {responseText}".Trim(), LogTarget.All);
        }
    }

    private Uri CreateUri(string relativePath) => new(GetBaseUri(), relativePath);

    private Uri GetBaseUri()
    {
        var configuredUrl = _settingsService.Current.AutoTorrent.QbittorrentWebUiUrl;
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("qBittorrent Web UI URL is invalid.");
        }

        var builder = new UriBuilder(baseUri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : 0;
    }

    private static long GetLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value)
            ? value
            : 0;
    }

    private static async Task<byte[]> ReadLimitedBytesAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > maxBytes)
            {
                throw new InvalidOperationException("Resolved torrent page is too large to inspect.");
            }

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static bool IsTorrentPayload(string contentType, byte[] bytes)
    {
        if (contentType.Contains("bittorrent", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("x-torrent", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return bytes.Length > 16 &&
               bytes[0] == (byte)'d' &&
               Encoding.ASCII.GetString(bytes.Take(Math.Min(bytes.Length, 512)).ToArray())
                   .Contains("announce", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeText(byte[] bytes, MediaTypeHeaderValue? contentType)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(contentType?.CharSet))
            {
                return Encoding.GetEncoding(contentType.CharSet.Trim('"')).GetString(bytes);
            }
        }
        catch
        {
            // Fall back to UTF-8 when the server reports an unsupported charset.
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static IReadOnlyList<TorrentAddSource> ExtractTorrentSources(string html, Uri baseUri)
    {
        var sources = new List<TorrentAddSource>();
        var decodedHtml = WebUtility.HtmlDecode(html);
        foreach (Match match in Regex.Matches(decodedHtml, "magnet:\\?[^\\s\"'<>]+", RegexOptions.IgnoreCase))
        {
            sources.Add(TorrentAddSource.FromUrl(match.Value, "magnet extracted from HTML"));
        }

        foreach (Match match in Regex.Matches(decodedHtml, "(?:href\\s*=\\s*[\"'](?<url>[^\"']+\\.torrent[^\"']*)[\"'])|(?<url>https?://[^\\s\"'<>]+\\.torrent[^\\s\"'<>]*)", RegexOptions.IgnoreCase))
        {
            var value = match.Groups["url"].Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var resolved = Uri.TryCreate(baseUri, value, out var resolvedUri)
                ? resolvedUri.ToString()
                : value;
            sources.Add(TorrentAddSource.FromUrl(resolved, ".torrent link extracted from HTML"));
        }

        return sources
            .GroupBy(source => source.Url ?? source.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static string GetTorrentFileName(Uri uri)
    {
        var fileName = Path.GetFileName(uri.AbsolutePath);
        return string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)
            ? "download.torrent"
            : fileName;
    }

    private static void AddStringContent(MultipartFormDataContent content, string name, string value)
    {
        content.Add(new StringContent(value), name);
    }

    private static bool IsSupportedTorrentUrl(string url)
    {
        if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("bc://bt/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https";
    }

    private sealed record SearchResultsResponse(string Status, IReadOnlyList<TorrentSearchResult> Results);

    private sealed record TorrentAddSource(string? Url, byte[]? TorrentBytes, string FileName, string Description)
    {
        public static TorrentAddSource FromUrl(string url, string description) => new(url, null, string.Empty, description);

        public static TorrentAddSource FromTorrentBytes(byte[] bytes, string fileName, string description) => new(null, bytes, fileName, description);
    }
}
