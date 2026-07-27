using System.Net.Http;
using System.Net.Http.Headers;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class JellyfinLibraryRefreshService : IJellyfinLibraryRefreshService
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(3);

    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _databaseService;
    private readonly IJellyfinClient _jellyfinClient;
    private readonly IWarpCliService _warpCliService;
    private readonly HttpClient _httpClient;
    private readonly IAppLogger _logger;
    private readonly object _queueLock = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    public JellyfinLibraryRefreshService(
        ISettingsService settingsService,
        IDatabaseService databaseService,
        IJellyfinClient jellyfinClient,
        IWarpCliService warpCliService,
        HttpClient httpClient,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;
        _jellyfinClient = jellyfinClient;
        _warpCliService = warpCliService;
        _httpClient = httpClient;
        _logger = logger;
    }

    public void EnqueueFromSourceItem(SourceItem item, TrackedShow? show = null)
    {
        if (!IsFeatureConfigured(out _))
        {
            return;
        }

        if (item.MediaKind != MediaKind.TvEpisode)
        {
            return;
        }

        show ??= TryResolveShow(item);
        if (show is null)
        {
            _logger.Debug(
                $"Jellyfin refresh skip '{item.FileName}': tracked show not resolved.",
                LogTarget.File | LogTarget.Console);
            return;
        }

        if (!show.IsAutoTracked)
        {
            _logger.Debug(
                $"Jellyfin refresh skip '{item.FileName}': show '{show.DisplayTitle}' is not Auto-Tracked.",
                LogTarget.File | LogTarget.Console);
            return;
        }

        if (show.UsesEpisodeGroup)
        {
            _logger.Info(
                $"Jellyfin refresh skip '{item.FileName}': episode-group order '{show.EpisodeGroupName ?? show.EpisodeGroupId}' (NFO-owned).",
                LogTarget.All);
            return;
        }

        var symlinkPath = item.SymlinkPath;
        if (string.IsNullOrWhiteSpace(symlinkPath))
        {
            _logger.Debug(
                $"Jellyfin refresh skip '{item.FileName}': symlink path missing.",
                LogTarget.File | LogTarget.Console);
            return;
        }

        EnqueuePaths([symlinkPath]);
    }

    public void EnqueuePaths(IEnumerable<string> symlinkPaths)
    {
        if (!IsFeatureConfigured(out _))
        {
            return;
        }

        var added = 0;
        lock (_queueLock)
        {
            foreach (var path in symlinkPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var normalized = Path.GetFullPath(path);
                if (_pendingPaths.Add(normalized))
                {
                    added++;
                }
            }

            if (added == 0 && _pendingPaths.Count == 0)
            {
                return;
            }

            ScheduleDebounceLocked();
        }

        if (added > 0)
        {
            _logger.Info(
                $"Jellyfin refresh queued {added} path(s); debounce {DebounceDelay.TotalSeconds:0}s (pending={_pendingPaths.Count}).",
                LogTarget.All);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        List<string> paths;
        lock (_queueLock)
        {
            CancelDebounceLocked();
            if (_pendingPaths.Count == 0)
            {
                _logger.Info("Jellyfin refresh flush: queue empty.", LogTarget.File | LogTarget.Console);
                return;
            }

            paths = _pendingPaths.ToList();
            _pendingPaths.Clear();
        }

        await FlushPathsAsync(paths, cancellationToken);
    }

    public Task<string> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _jellyfinClient.TestConnectionAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_queueLock)
        {
            CancelDebounceLocked();
        }

        _flushGate.Dispose();
    }

    private void ScheduleDebounceLocked()
    {
        CancelDebounceLocked();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, cts.Token);
                await FlushAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error("Jellyfin refresh debounce flush failed.", ex, LogTarget.All);
            }
        });
    }

    private void CancelDebounceLocked()
    {
        if (_debounceCts is null)
        {
            return;
        }

        try
        {
            _debounceCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _debounceCts.Dispose();
        _debounceCts = null;
    }

    private async Task FlushPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        if (!IsFeatureConfigured(out var jellyfinSettings))
        {
            _logger.Info(
                $"Jellyfin refresh skipped {paths.Count} path(s): feature disabled or not configured.",
                LogTarget.All);
            return;
        }

        await _flushGate.WaitAsync(cancellationToken);
        var warpOwnedByUs = false;
        try
        {
            _logger.Info(
                $"Jellyfin refresh flush starting for {paths.Count} path(s) → {jellyfinSettings.BaseUrl}.",
                LogTarget.All);

            await EnsureTmdbReachableWithWarpAsync(
                getWarpOwned: () => warpOwnedByUs,
                setWarpOwned: value => warpOwnedByUs = value,
                cancellationToken);

            await _jellyfinClient.ReportMediaUpdatedAsync(paths, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"Jellyfin refresh flush failed for {paths.Count} path(s).", ex, LogTarget.All);
            throw;
        }
        finally
        {
            if (warpOwnedByUs)
            {
                _logger.Info("Disconnecting WARP after Jellyfin refresh (owned session).", LogTarget.All);
                try
                {
                    await _warpCliService.DisconnectAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"WARP disconnect after Jellyfin refresh failed: {ex.Message}", LogTarget.All);
                }
            }

            _flushGate.Release();
        }
    }

    private async Task EnsureTmdbReachableWithWarpAsync(
        Func<bool> getWarpOwned,
        Action<bool> setWarpOwned,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settingsService.Current.TmdbReadAccessToken))
        {
            _logger.Warning(
                "TMDB read access token missing; skipping pre-probe before Jellyfin refresh.",
                LogTarget.All);
            return;
        }

        try
        {
            await ProbeTmdbAsync(cancellationToken);
            _logger.Info("TMDB pre-probe OK before Jellyfin refresh.", LogTarget.All);
            return;
        }
        catch (Exception ex) when (CanAutoRecoverOnSsl() && SslTlsErrorDetector.IsSslOrTlsError(ex))
        {
            _logger.Warning(
                $"TMDB SSL/TLS error before Jellyfin refresh: {ex.Message}. Attempting WARP self-recover.",
                LogTarget.All);
        }

        if (!getWarpOwned())
        {
            var timeout = TimeSpan.FromSeconds(_settingsService.Current.Warp?.ConnectTimeoutSeconds ?? 30);
            var attempt = await _warpCliService.ConnectOwnedAsync(timeout, cancellationToken);
            if (!attempt.Connected)
            {
                _logger.Warning(
                    "WARP connect for Jellyfin refresh SSL recover failed or timed out.",
                    LogTarget.All);
                throw new InvalidOperationException("TMDB unreachable (SSL) and WARP connect failed.");
            }

            if (attempt.Owned)
            {
                setWarpOwned(true);
                _logger.Info("WARP connected for Jellyfin refresh SSL recover.", LogTarget.All);
            }
            else
            {
                _logger.Info(
                    "WARP already connected; Jellyfin refresh will leave the existing session open.",
                    LogTarget.All);
            }
        }

        _logger.Info("Retrying TMDB pre-probe with WARP before Jellyfin refresh.", LogTarget.All);
        await ProbeTmdbAsync(cancellationToken);
        _logger.Info("TMDB pre-probe OK after WARP.", LogTarget.All);
    }

    private async Task ProbeTmdbAsync(CancellationToken cancellationToken)
    {
        var token = _settingsService.Current.TmdbReadAccessToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("TMDB read access token is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.themoviedb.org/3/configuration");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"TMDB probe failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
    }

    private bool CanAutoRecoverOnSsl()
    {
        var warp = _settingsService.Current.Warp ?? new WarpSettings();
        return warp.Enabled && warp.AutoRecoverOnSsl && _warpCliService.IsAvailable;
    }

    private bool IsFeatureConfigured(out JellyfinRefreshSettings settings)
    {
        settings = _settingsService.Current.AutoTrack?.Jellyfin ?? new JellyfinRefreshSettings();
        return settings.Enabled &&
               !string.IsNullOrWhiteSpace(settings.BaseUrl) &&
               !string.IsNullOrWhiteSpace(settings.ApiKey);
    }

    private TrackedShow? TryResolveShow(SourceItem item)
    {
        if (!string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(item.ProviderId, out var tmdbId))
        {
            return null;
        }

        return _databaseService.GetTrackedShowByTmdbId(tmdbId);
    }
}
