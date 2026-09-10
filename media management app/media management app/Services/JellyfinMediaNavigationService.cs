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
        IAppLifecycleService lifecycleService,
        IAppLogger logger)
    {
        _jellyfinClient = jellyfinClient;
        _jellyfinViewerService = jellyfinViewerService;
        _settingsService = settingsService;
        _logger = logger;
        lifecycleService.AppModeChanged += OnAppModeChanged;
    }

    public Task<JellyfinMediaNavigationResult> OpenEpisodeAsync(
        JellyfinEpisodeTarget target,
        CancellationToken cancellationToken = default) =>
        OpenAsync(JellyfinMediaTarget.FromEpisode(target), cancellationToken);

    public void ClearCache()
    {
        var count = _itemIdCache.Count;
        _itemIdCache.Clear();
        _logger.Info(
            $"Jellyfin media navigation: cleared {count} cached item id(s).",
            LogTarget.All);
    }

    public async Task<JellyfinMediaNavigationResult> OpenAsync(
        JellyfinMediaTarget target,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateTarget(target, out var validationError))
        {
            return JellyfinMediaNavigationResult.Fail(validationError);
        }

        if (!TryGetConfiguredBaseUri(out var baseUri, out var configError))
        {
            _logger.Warning($"Jellyfin {FormatKind(target.Kind)} navigation: {configError}", LogTarget.All);
            return JellyfinMediaNavigationResult.Fail(configError);
        }

        try
        {
            var cacheKey = BuildCacheKey(baseUri, target);
            var label = FormatLabel(target);
            string? itemId;
            if (_itemIdCache.TryGetValue(cacheKey, out itemId) &&
                !string.IsNullOrWhiteSpace(itemId))
            {
                _logger.Info(
                    $"Jellyfin {FormatKind(target.Kind)} navigation: cache hit for TMDB {target.TmdbId} {label} (item {itemId}).",
                    LogTarget.All);
            }
            else
            {
                itemId = await LookupAndCacheAsync(cacheKey, target, label, cancellationToken);
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    return FailNotIndexed(target.Kind, label);
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
            _logger.Warning($"Jellyfin {FormatKind(target.Kind)} navigation failed: {ex.Message}", LogTarget.All);
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
        JellyfinMediaTarget target,
        string label,
        CancellationToken cancellationToken)
    {
        _logger.Info(
            $"Jellyfin {FormatKind(target.Kind)} navigation: lookup for TMDB {target.TmdbId} {label}.",
            LogTarget.All);
        var itemId = target.Kind switch
        {
            JellyfinMediaKind.Series => await _jellyfinClient.FindSeriesItemIdAsync(target.TmdbId, cancellationToken),
            JellyfinMediaKind.Movie => await _jellyfinClient.FindMovieItemIdAsync(target.TmdbId, cancellationToken),
            JellyfinMediaKind.Episode => await _jellyfinClient.FindEpisodeItemIdAsync(
                target.TmdbId,
                target.SeasonNumber!.Value,
                target.EpisodeNumber!.Value,
                cancellationToken),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        _itemIdCache[cacheKey] = itemId;
        return itemId;
    }

    private JellyfinMediaNavigationResult FailNotIndexed(JellyfinMediaKind kind, string label)
    {
        var missing = $"{label} is available locally but has not been indexed by Jellyfin yet.";
        _logger.Warning($"Jellyfin {FormatKind(kind)} navigation: {missing}", LogTarget.All);
        return JellyfinMediaNavigationResult.Fail(missing);
    }

    private static bool TryValidateTarget(JellyfinMediaTarget target, out string errorMessage)
    {
        if (target.TmdbId <= 0)
        {
            errorMessage = target.Kind switch
            {
                JellyfinMediaKind.Movie => "Movie is missing a TMDB id.",
                JellyfinMediaKind.Series => "Show is missing a TMDB id.",
                _ => "Episode is missing a TMDB show id."
            };
            return false;
        }

        if (target.Kind == JellyfinMediaKind.Episode &&
            (target.SeasonNumber is null || target.EpisodeNumber is null))
        {
            errorMessage = "Episode is missing a season or episode number.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static string FormatKind(JellyfinMediaKind kind) => kind switch
    {
        JellyfinMediaKind.Series => "series",
        JellyfinMediaKind.Movie => "movie",
        _ => "episode"
    };

    private static string FormatLabel(JellyfinMediaTarget target)
    {
        if (target.Kind == JellyfinMediaKind.Episode &&
            target.SeasonNumber is int season &&
            target.EpisodeNumber is int episode)
        {
            return $"S{season:00}E{episode:00}";
        }

        return string.IsNullOrWhiteSpace(target.DisplayName)
            ? $"TMDB {target.TmdbId}"
            : target.DisplayName.Trim();
    }

    private static string BuildCacheKey(Uri baseUri, JellyfinMediaTarget target)
    {
        var origin = $"{baseUri.Scheme}://{baseUri.Host}:{baseUri.Port}";
        return $"{origin}|{target.Kind}|{target.TmdbId}|{target.SeasonNumber}|{target.EpisodeNumber}";
    }

    private static Uri BuildDetailsUri(Uri baseUri, string itemId)
    {
        var origin = baseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return new Uri($"{origin}/web/#/details?id={Uri.EscapeDataString(itemId)}");
    }

    private void OnAppModeChanged(object? sender, AppMode mode)
    {
        if (mode != AppMode.Background)
        {
            return;
        }

        ClearCache();
    }
}
