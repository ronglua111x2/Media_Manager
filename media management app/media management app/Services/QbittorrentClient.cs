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
    private const int MinSearchTimeoutSeconds = 10;
    private const int MaxSearchTimeoutSeconds = 300;
    private const int AddVerifyTimeoutSeconds = 20;
    private const int MaxResolverBytes = 2 * 1024 * 1024;

    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private bool _isLoggedIn;
    private string? _loginSessionKey;

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
        var probe = await ProbeWebUiAsync(cancellationToken);
        if (!probe.IsOk)
        {
            throw new InvalidOperationException(probe.Detail ?? $"qBittorrent connection test failed: {probe.Status}");
        }

        return probe.Version ?? string.Empty;
    }

    public async Task<QbittorrentWebUiProbeResult> ProbeWebUiAsync(CancellationToken cancellationToken = default)
    {
        Uri baseUri;
        try
        {
            baseUri = GetBaseUri();
        }
        catch (InvalidOperationException ex)
        {
            return QbittorrentWebUiProbeResult.InvalidUrl(ex.Message);
        }

        // Any HTTP response from /app/version means the WebUI is bound (even 401/403).
        try
        {
            using var versionResponse = await _httpClient.GetAsync(
                new Uri(baseUri, "api/v2/app/version"),
                cancellationToken);

            if (IsAuthenticationFailure(versionResponse.StatusCode))
            {
                // Bound but needs auth — try login for a clearer Ok vs AuthFailed.
            }
            else if (versionResponse.IsSuccessStatusCode)
            {
                // May succeed without login on some setups; still verify login when credentials exist.
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested && ex is OperationCanceledException)
            {
                throw;
            }

            return QbittorrentWebUiProbeResult.Unreachable(
                $"qBittorrent WebUI unreachable: {ex.Message}");
        }

        try
        {
            await LoginAsync(cancellationToken, force: true);
            using var response = await _httpClient.GetAsync(CreateUri("api/v2/app/version"), cancellationToken);
            if (IsAuthenticationFailure(response.StatusCode))
            {
                return QbittorrentWebUiProbeResult.AuthFailed(
                    $"qBittorrent WebUI auth failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            if (!response.IsSuccessStatusCode)
            {
                // Bound (got HTTP) but not healthy enough to use — treat as auth/config, not restart.
                return QbittorrentWebUiProbeResult.AuthFailed(
                    $"qBittorrent WebUI returned {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var version = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            return QbittorrentWebUiProbeResult.Ok(version);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("login failed", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("authentication failed", StringComparison.OrdinalIgnoreCase))
        {
            return QbittorrentWebUiProbeResult.AuthFailed(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested && ex is OperationCanceledException)
            {
                throw;
            }

            return QbittorrentWebUiProbeResult.Unreachable(
                $"qBittorrent WebUI unreachable: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(TorrentSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new InvalidOperationException("Search query is empty.");
        }

        await LoginAsync(cancellationToken);
        int? searchId = null;
        var timeoutSeconds = Math.Clamp(request.TimeoutSeconds, MinSearchTimeoutSeconds, MaxSearchTimeoutSeconds);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        var idleTimeoutSeconds = Math.Clamp(request.IdleTimeoutSeconds, RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds, RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds);
        var idleTimeout = idleTimeoutSeconds > 0 ? TimeSpan.FromSeconds(idleTimeoutSeconds) : (TimeSpan?)null;
        IReadOnlyList<TorrentSearchResult> latestResults = [];
        var latestStatus = "Running";
        var mergedByUrl = new Dictionary<string, TorrentSearchResult>(StringComparer.OrdinalIgnoreCase);
        var lastMergedCount = 0;
        var idleDeadline = idleTimeout is null ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.Add(idleTimeout.Value);
        var endedBy = "timeout";

        try
        {
            searchId = await StartSearchAsync(request, cancellationToken);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var response = await GetSearchResultsAsync(searchId.Value, request.Limit, request.Offset, cancellationToken);
                latestResults = response.Results;
                latestStatus = response.Status;
                foreach (var result in response.Results)
                {
                    if (string.IsNullOrWhiteSpace(result.FileUrl))
                    {
                        continue;
                    }

                    if (!mergedByUrl.TryGetValue(result.FileUrl, out var existing) ||
                        result.Seeders > existing.Seeders)
                    {
                        mergedByUrl[result.FileUrl] = result;
                    }
                }

                if (mergedByUrl.Count > lastMergedCount)
                {
                    lastMergedCount = mergedByUrl.Count;
                    if (idleTimeout is not null)
                    {
                        idleDeadline = DateTimeOffset.UtcNow.Add(idleTimeout.Value);
                    }
                }
                else if (idleTimeout is not null && DateTimeOffset.UtcNow >= idleDeadline)
                {
                    endedBy = "idle-timeout";
                    break;
                }

                if (!string.Equals(latestStatus, "Running", StringComparison.OrdinalIgnoreCase))
                {
                    endedBy = "status-finished";
                    break;
                }

                await Task.Delay(SearchPollDelayMilliseconds, cancellationToken);
            }

            var mergedResults = mergedByUrl.Values
                .OrderByDescending(result => result.Seeders)
                .ThenBy(result => result.FileSize)
                .ToList();
            if (endedBy == "timeout" && mergedResults.Count > 0 && mergedResults.Count >= request.Limit)
            {
                endedBy = "max-results";
            }

            _logger.Info(
                $"qBittorrent search completed. Query='{request.Query}', Status='{latestStatus}', Results={mergedResults.Count}, TimeoutSeconds={timeoutSeconds}, IdleTimeoutSeconds={idleTimeoutSeconds}, EndedBy='{endedBy}'.",
                LogTarget.All);

            return mergedResults;
        }
        finally
        {
            if (searchId is not null)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    await StopSearchAsync(searchId.Value, CancellationToken.None);
                }

                await DeleteSearchAsync(searchId.Value, CancellationToken.None);
            }
        }
    }

    public async Task<TorrentMetadataProbeResult> ProbeTorrentMetadataAsync(TorrentSearchResult result, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(result.FileUrl))
        {
            return new TorrentMetadataProbeResult { IsAvailable = false, Reason = "candidate URL is empty" };
        }

        if (result.FileUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            return new TorrentMetadataProbeResult { IsAvailable = false, Reason = "magnet metadata is unavailable before add" };
        }

        try
        {
            var sources = await ResolveAddSourcesAsync(result.FileUrl, cancellationToken);
            foreach (var source in sources)
            {
                var bytes = source.TorrentBytes;
                if (bytes is null &&
                    !string.IsNullOrWhiteSpace(source.Url) &&
                    Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) &&
                    uri.Scheme is "http" or "https")
                {
                    bytes = await TryDownloadTorrentPayloadAsync(uri, cancellationToken);
                }

                if (bytes is null)
                {
                    continue;
                }

                var metadata = TorrentMetadataReader.Read(bytes);
                _logger.Debug(
                    $"Probed torrent metadata. Candidate='{result.FileName}', TorrentName='{metadata.TorrentName}', Files={metadata.Files.Count}, VideoFiles={metadata.VideoFileCount}, Size={metadata.TotalSize}.",
                    LogTarget.File | LogTarget.Console);
                return metadata;
            }

            return new TorrentMetadataProbeResult { IsAvailable = false, Reason = "no .torrent payload could be resolved" };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException)
        {
            _logger.Warning($"Torrent metadata probe failed for '{result.FileName}'. Url='{result.FileUrl}', Error='{ex.Message}'", LogTarget.All);
            return new TorrentMetadataProbeResult { IsAvailable = false, Reason = ex.Message };
        }
    }

    public async Task<AddedTorrentResult> AddTorrentAsync(AddTorrentRequest request, CancellationToken cancellationToken = default)
    {
        var torrentUrl = TorrentSearchResult.NormalizeUrl(request.Url);
        if (string.IsNullOrWhiteSpace(torrentUrl))
        {
            throw new InvalidOperationException("Torrent URL is empty.");
        }
        if (!IsSupportedTorrentUrl(torrentUrl))
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

        if (!string.IsNullOrWhiteSpace(request.PluginName) && !ShouldSkipSearchPluginDownload(torrentUrl))
        {
            _logger.Info($"Trying qBittorrent search plugin download. Plugin='{request.PluginName}', Url='{torrentUrl}'", LogTarget.All);
            await PostSearchDownloadTorrentAsync(torrentUrl, request.PluginName, cancellationToken);
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
        else if (!string.IsNullOrWhiteSpace(request.PluginName) && ShouldSkipSearchPluginDownload(torrentUrl))
        {
            _logger.Info(
                $"Skipping qBittorrent search plugin for details-page URL. Resolving magnet or .torrent directly. Url='{torrentUrl}'.",
                LogTarget.All);
        }

        _logger.Info($"Resolving add sources for torrent. Url='{torrentUrl}', SavePath='{savePath ?? "(default)"}'.", LogTarget.All);
        var addSources = await ResolveAddSourcesAsync(torrentUrl, cancellationToken);
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
        _loginGate.Dispose();
        _httpClient.Dispose();
    }

    private async Task LoginAsync(CancellationToken cancellationToken, bool force = false)
    {
        var settings = _settingsService.Current.AutoTorrent;
        var sessionKey = $"{settings.QbittorrentWebUiUrl}|{settings.Username}|{settings.Password}";
        await _loginGate.WaitAsync(cancellationToken);
        try
        {
            if (!force && _isLoggedIn && string.Equals(_loginSessionKey, sessionKey, StringComparison.Ordinal))
            {
                return;
            }

            _cookies.GetCookies(GetBaseUri()).Clear();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = settings.Username ?? string.Empty,
                ["password"] = settings.Password ?? string.Empty
            });

            using var response = await _httpClient.PostAsync(CreateUri("api/v2/auth/login"), content, cancellationToken);
            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (!response.IsSuccessStatusCode || !body.Equals("Ok.", StringComparison.OrdinalIgnoreCase))
            {
                MarkLoggedOut();
                throw new InvalidOperationException("qBittorrent login failed. Check Web UI URL, username, password, and Web UI settings.");
            }

            _isLoggedIn = true;
            _loginSessionKey = sessionKey;
        }
        finally
        {
            _loginGate.Release();
        }
    }

    private void MarkLoggedOut()
    {
        _isLoggedIn = false;
        _loginSessionKey = null;
    }

    private static bool IsAuthenticationFailure(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized;
    }

    private static bool IsSearchCapacityFailure(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.Conflict;
    }

    private async Task<HttpResponseMessage> PostFormWithAuthRetryAsync(
        string relativePath,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var content = new FormUrlEncodedContent(form);
            var response = await _httpClient.PostAsync(CreateUri(relativePath), content, cancellationToken);
            if (!IsAuthenticationFailure(response.StatusCode))
            {
                return response;
            }

            response.Dispose();
            MarkLoggedOut();
            await LoginAsync(cancellationToken, force: true);
        }

        throw new InvalidOperationException("qBittorrent authentication failed after retry.");
    }

    private async Task<HttpResponseMessage> GetWithAuthRetryAsync(string relativePath, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await _httpClient.GetAsync(CreateUri(relativePath), cancellationToken);
            if (!IsAuthenticationFailure(response.StatusCode))
            {
                return response;
            }

            response.Dispose();
            MarkLoggedOut();
            await LoginAsync(cancellationToken, force: true);
        }

        throw new InvalidOperationException("qBittorrent authentication failed after retry.");
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

    public async Task<IReadOnlyList<AddedTorrentResult>> GetTorrentsAsync(CancellationToken cancellationToken = default)
    {
        await LoginAsync(cancellationToken);
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
                Progress = GetDouble(torrentElement, "progress"),
                SavePath = GetString(torrentElement, "save_path") ?? string.Empty,
                Category = GetString(torrentElement, "category") ?? string.Empty
            });
        }

        return torrents;
    }

    public async Task<IReadOnlyList<TorrentContentFile>> GetTorrentFilesAsync(string hash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return [];
        }

        await LoginAsync(cancellationToken);
        using var response = await _httpClient.GetAsync(CreateUri($"api/v2/torrents/files?hash={Uri.EscapeDataString(hash)}"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var files = new List<TorrentContentFile>();
        foreach (var fileElement in document.RootElement.EnumerateArray())
        {
            files.Add(new TorrentContentFile
            {
                Name = GetString(fileElement, "name") ?? string.Empty,
                Size = GetLong(fileElement, "size"),
                Progress = GetDouble(fileElement, "progress"),
                Priority = GetInt(fileElement, "priority"),
                IsSeed = GetBool(fileElement, "is_seed")
            });
        }

        return files;
    }

    private async Task<AddedTorrentResult?> GetTorrentAsync(string hash, CancellationToken cancellationToken)
    {
        return (await GetTorrentsAsync(cancellationToken))
            .FirstOrDefault(torrent => string.Equals(torrent.Hash, hash, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<int> StartSearchAsync(TorrentSearchRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await PostFormWithAuthRetryAsync("api/v2/search/start", new Dictionary<string, string>
        {
            ["pattern"] = request.Query,
            ["plugins"] = request.Plugins,
            ["category"] = request.Category
        }, cancellationToken);
        if (IsSearchCapacityFailure(response.StatusCode))
        {
            throw new QbittorrentSearchCapacityException();
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id))
        {
            throw new InvalidOperationException("qBittorrent did not return a valid search id.");
        }

        return id;
    }

    public async Task<SearchJobResults> GetSearchResultsAsync(int searchId, int limit, int offset = 0, CancellationToken cancellationToken = default)
    {
        var path = $"api/v2/search/results?id={searchId}&limit={Math.Max(limit, 1)}&offset={Math.Max(offset, 0)}";
        using var response = await GetWithAuthRetryAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var status = GetString(root, "status") ?? "Unknown";
        var total = GetInt(root, "total");
        var results = new List<TorrentSearchResult>();

        if (root.TryGetProperty("results", out var resultsElement) && resultsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var resultElement in resultsElement.EnumerateArray())
            {
                var descriptionUrl = GetString(resultElement, "descrLink") ?? string.Empty;
                var fileUrl = GetString(resultElement, "fileUrl") ?? string.Empty;
                var candidateUrl = !string.IsNullOrWhiteSpace(fileUrl) ? fileUrl : descriptionUrl;
                if (string.IsNullOrWhiteSpace(candidateUrl))
                {
                    continue;
                }

                results.Add(new TorrentSearchResult
                {
                    FileName = GetString(resultElement, "fileName") ?? string.Empty,
                    FileSize = GetLong(resultElement, "fileSize"),
                    FileUrl = candidateUrl,
                    DescriptionUrl = TorrentSearchResult.NormalizeUrl(descriptionUrl),
                    Seeders = GetInt(resultElement, "nbSeeders"),
                    Leechers = GetInt(resultElement, "nbLeechers"),
                    EngineName = GetString(resultElement, "engineName") ?? string.Empty,
                    SiteUrl = GetString(resultElement, "siteUrl") ?? string.Empty
                });
            }
        }

        return new SearchJobResults(status, results, total);
    }

    public async Task StopSearchAsync(int searchId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await PostFormWithAuthRetryAsync("api/v2/search/stop", new Dictionary<string, string>
            {
                ["id"] = searchId.ToString()
            }, cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                _logger.Warning($"Failed to stop qBittorrent search {searchId}: {(int)response.StatusCode} {response.ReasonPhrase}", LogTarget.All);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to stop qBittorrent search {searchId}: {ex.Message}", LogTarget.All);
        }
    }

    public async Task DeleteSearchAsync(int searchId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await PostFormWithAuthRetryAsync("api/v2/search/delete", new Dictionary<string, string>
            {
                ["id"] = searchId.ToString()
            }, cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                _logger.Warning($"Failed to delete qBittorrent search {searchId}: {(int)response.StatusCode} {response.ReasonPhrase}", LogTarget.All);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to delete qBittorrent search {searchId}: {ex.Message}", LogTarget.All);
        }
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

    private async Task<byte[]?> TryDownloadTorrentPayloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("MediaManager/1.0");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await ReadLimitedBytesAsync(response.Content, MaxResolverBytes, cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        return IsTorrentPayload(contentType, bytes) ? bytes : null;
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

    private static double GetDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)
            ? value
            : 0;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.True;
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

    private static bool ShouldSkipSearchPluginDownload(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        return path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("-torrent-", StringComparison.OrdinalIgnoreCase);
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

    private sealed record TorrentAddSource(string? Url, byte[]? TorrentBytes, string FileName, string Description)
    {
        public static TorrentAddSource FromUrl(string url, string description) => new(url, null, string.Empty, description);

        public static TorrentAddSource FromTorrentBytes(byte[] bytes, string fileName, string description) => new(null, bytes, fileName, description);
    }

    public async Task DeleteTorrentsAsync(IEnumerable<string> hashes, bool deleteFiles = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var hashList = hashes.ToList();
            if (hashList.Count == 0)
            {
                return;
            }

            var hashesStr = string.Join("|", hashList);
            using var response = await PostFormWithAuthRetryAsync("api/v2/torrents/delete", new Dictionary<string, string>
            {
                ["hashes"] = hashesStr,
                ["deleteFiles"] = deleteFiles ? "true" : "false"
            }, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning($"Failed to delete torrents from qBittorrent: {(int)response.StatusCode} {response.ReasonPhrase}", LogTarget.All);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error deleting torrents: {ex.Message}", LogTarget.All);
        }
    }

    public async Task PauseTorrentsAsync(IEnumerable<string> hashes, CancellationToken cancellationToken = default)
    {
        var hashList = hashes.Where(hash => !string.IsNullOrWhiteSpace(hash)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hashList.Count == 0)
        {
            return;
        }

        var form = new Dictionary<string, string>
        {
            ["hashes"] = string.Join("|", hashList)
        };

        // qBittorrent 5 uses stop; 4.x uses pause.
        using var stopResponse = await PostFormWithAuthRetryAsync("api/v2/torrents/stop", form, cancellationToken);
        if (stopResponse.IsSuccessStatusCode)
        {
            return;
        }

        using var pauseResponse = await PostFormWithAuthRetryAsync("api/v2/torrents/pause", form, cancellationToken);
        if (!pauseResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to pause torrents in qBittorrent: {(int)pauseResponse.StatusCode} {pauseResponse.ReasonPhrase}");
        }
    }

    public async Task ResumeTorrentsAsync(IEnumerable<string> hashes, CancellationToken cancellationToken = default)
    {
        var hashList = hashes.Where(hash => !string.IsNullOrWhiteSpace(hash)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hashList.Count == 0)
        {
            return;
        }

        using var response = await PostFormWithAuthRetryAsync("api/v2/torrents/start", new Dictionary<string, string>
        {
            ["hashes"] = string.Join("|", hashList)
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to resume torrents in qBittorrent: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
    }
}
