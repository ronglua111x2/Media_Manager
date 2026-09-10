using System.Collections.Concurrent;
using media_management_app.Common;
using media_management_app.Models;
using WpfApplication = System.Windows.Application;

namespace media_management_app.Services;

public sealed class JellyfinMediaNavigationService : IJellyfinMediaNavigationService
{
    private readonly IJellyfinClient _jellyfinClient;
    private readonly IJellyfinViewerService _jellyfinViewerService;
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<string, string> _itemIdCache = new(StringComparer.OrdinalIgnoreCase);

    public JellyfinMediaNavigationService(
        IJellyfinClient jellyfinClient,
        IJellyfinViewerService jellyfinViewerService,
        ISettingsService settingsService,
        IAppLogger logger)
    {
        _jellyfinClient = jellyfinClient;
        _jellyfinViewerService = jellyfinViewerService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<JellyfinMediaNavigationResult> OpenEpisodeAsync(
        JellyfinEpisodeTarget target,
        CancellationToken cancellationToken = default)
    {
        if (target.ShowTmdbId <= 0)
        {
            return JellyfinMediaNavigationResult.Fail("Episode is missing a TMDB show id.");
        }

        if (!TryGetConfiguredBaseUri(out var baseUri, out var configError))
        {
            _logger.Warning($"Jellyfin episode navigation: {configError}", LogTarget.All);
            return JellyfinMediaNavigationResult.Fail(configError);
        }

        try
        {
            var cacheKey = BuildCacheKey(baseUri, target);
            var episodeLabel = FormatEpisodeLabel(target);
            string? itemId;
            if (_itemIdCache.TryGetValue(cacheKey, out itemId) &&
                !string.IsNullOrWhiteSpace(itemId))
            {
                _logger.Info(
                    $"Jellyfin episode navigation: cache hit for TMDB {target.ShowTmdbId} {episodeLabel} (item {itemId}).",
                    LogTarget.All);
            }
            else
            {
                itemId = await LookupAndCacheAsync(cacheKey, target, episodeLabel, cancellationToken);
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    return FailNotIndexed(episodeLabel);
                }
            }

            var detailsUri = BuildDetailsUri(baseUri, itemId);
            var chromeDataContext = WpfApplication.Current?.MainWindow?.DataContext;
            await _jellyfinViewerService.ShowOrNavigateAsync(detailsUri, chromeDataContext);
            return JellyfinMediaNavigationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Jellyfin episode navigation failed: {ex.Message}", LogTarget.All);
            return JellyfinMediaNavigationResult.Fail(ex.Message);
        }
    }

    private bool TryGetConfiguredBaseUri(out Uri baseUri, out string errorMessage)
    {
        var settings = _settingsService.Current.AutoTrack?.Jellyfin ?? new JellyfinRefreshSettings();
        if (string.IsNullOrWhiteSpace(settings.BaseUrl)
            || !Uri.TryCreate(settings.BaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var parsed))
        {
            baseUri = new Uri("about:blank");
            errorMessage = "Jellyfin base URL is invalid. Check Integrations settings.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            baseUri = parsed;
            errorMessage = "Jellyfin API key is not configured. Check Integrations settings.";
            return false;
        }

        baseUri = parsed;
        errorMessage = string.Empty;
        return true;
    }

    private async Task<string?> LookupAndCacheAsync(
        string cacheKey,
        JellyfinEpisodeTarget target,
        string episodeLabel,
        CancellationToken cancellationToken)
    {
        _logger.Info(
            $"Jellyfin episode navigation: lookup for TMDB {target.ShowTmdbId} {episodeLabel}.",
            LogTarget.All);
        var itemId = await _jellyfinClient.FindEpisodeItemIdAsync(
            target.ShowTmdbId,
            target.SeasonNumber,
            target.EpisodeNumber,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        _itemIdCache[cacheKey] = itemId;
        return itemId;
    }

    private JellyfinMediaNavigationResult FailNotIndexed(string episodeLabel)
    {
        var missing = $"{episodeLabel} is available locally but has not been indexed by Jellyfin yet.";
        _logger.Warning($"Jellyfin episode navigation: {missing}", LogTarget.All);
        return JellyfinMediaNavigationResult.Fail(missing);
    }

    private static string FormatEpisodeLabel(JellyfinEpisodeTarget target)
        => $"S{target.SeasonNumber:00}E{target.EpisodeNumber:00}";

    private static string BuildCacheKey(Uri baseUri, JellyfinEpisodeTarget target)
    {
        var origin = $"{baseUri.Scheme}://{baseUri.Host}:{baseUri.Port}";
        return $"{origin}|{target.ShowTmdbId}|{target.SeasonNumber}|{target.EpisodeNumber}";
    }

    private static Uri BuildDetailsUri(Uri baseUri, string itemId)
    {
        var origin = baseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return new Uri($"{origin}/web/#/details?id={Uri.EscapeDataString(itemId)}");
    }
}
