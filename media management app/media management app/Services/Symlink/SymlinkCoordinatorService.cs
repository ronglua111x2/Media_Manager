using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services.Events;

namespace media_management_app.Services.Symlink;

public sealed class SymlinkCoordinatorService : ISymlinkCoordinatorService
{
    private static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(8);

    private readonly ISettingsService _settingsService;
    private readonly ISymlinkSyncService _symlinkSyncService;
    private readonly ILibraryLinkEventHub _eventHub;
    private readonly IDatabaseService _databaseService;
    private readonly IPosterImageService _posterImageService;
    private readonly IWindowsNotificationService _windowsNotificationService;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _startLock = new();
    private readonly object _disposeLock = new();
    private Task? _startupWorker;
    private bool _disposed;

    public SymlinkCoordinatorService(
        ISettingsService settingsService,
        ISymlinkSyncService symlinkSyncService,
        ILibraryLinkEventHub eventHub,
        IDatabaseService databaseService,
        IPosterImageService posterImageService,
        IWindowsNotificationService windowsNotificationService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _symlinkSyncService = symlinkSyncService;
        _eventHub = eventHub;
        _databaseService = databaseService;
        _posterImageService = posterImageService;
        _windowsNotificationService = windowsNotificationService;
        _logger = logger;

        _eventHub.HardlinkCreated += OnHardlinkCreated;
        _eventHub.HardlinkRemoved += OnHardlinkRemoved;
    }

    public void Start()
    {
        lock (_startLock)
        {
            _startupWorker ??= Task.Run(RunStartupReconcileAsync);
        }
    }

    public async Task<SymlinkSyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(_symlinkSyncService.ReconcileAll, cancellationToken);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _eventHub.HardlinkCreated -= OnHardlinkCreated;
        _eventHub.HardlinkRemoved -= OnHardlinkRemoved;

        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            if (_startupWorker is not null && !_startupWorker.Wait(ShutdownWaitTimeout))
            {
                _logger.Warning(
                    $"Symlink coordinator did not stop within {ShutdownWaitTimeout.TotalSeconds:0} seconds.",
                    LogTarget.All);
            }
        }
        catch (AggregateException)
        {
        }

        try
        {
            _shutdown.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        _syncGate.Dispose();
    }

    private void OnHardlinkCreated(object? sender, LibraryLinkEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessHardlinkCreatedAsync(e, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error($"Symlink coordinator failed after hardlink created for {e.Item.FilePath}", ex, LogTarget.All);
            }
        });
    }

    private void OnHardlinkRemoved(object? sender, LibraryLinkEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessHardlinkRemovedAsync(e, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error($"Symlink coordinator failed after hardlink removed for {e.Item.FilePath}", ex, LogTarget.All);
            }
        });
    }

    private async Task RunStartupReconcileAsync()
    {
        try
        {
            if (!_settingsService.Current.Symlink.SyncOnStartup)
            {
                _logger.Info("Symlink startup reconcile skipped because SyncOnStartup is disabled.", LogTarget.All);
                return;
            }

            await SyncNowAsync(_shutdown.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("Symlink startup reconcile failed.", ex, LogTarget.All);
        }
    }

    private async Task ProcessHardlinkCreatedAsync(LibraryLinkEventArgs e, CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var result = await Task.Run(() => _symlinkSyncService.SyncItem(e.Item, e.LinkedPath), cancellationToken);
            if (result.CreatedCount > 0 || result.RepairedCount > 0 || result.ErrorCount > 0)
            {
                _logger.Info($"Symlink event sync for {e.Item.FileName}. {result.Summary}", LogTarget.All);
            }

            if (result.CreatedCount > 0 || result.RepairedCount > 0)
            {
                NotifySymlinkedItem(e.Item);
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private void NotifySymlinkedItem(SourceItem item)
    {
        var message = BuildSymlinkNotificationMessage(item);
        var poster = GetNotificationPoster(item);
        _windowsNotificationService.TryShow(new WindowsNotificationRequest
        {
            Title = "Media Manager",
            Message = message,
            HeroImagePathOrUrl = poster,
            AppLogoOverridePathOrUrl = poster
        });
    }

    private string BuildSymlinkNotificationMessage(SourceItem item)
    {
        if (item.MediaKind == MediaKind.Movie)
        {
            var title = item.MovieTitle ?? item.MatchedTitle ?? item.ShowTitle ?? "Unknown Movie";
            return $"{title} — Symlinked";
        }

        var showTitle = item.MatchedTitle ?? item.ShowTitle ?? "Unknown Show";
        return $"{showTitle} — Symlinked {FormatEpisodeLabel(item)}";
    }

    private static string FormatEpisodeLabel(SourceItem item)
    {
        var seasonNumber = item.MappedSeasonNumber ?? item.SeasonNumber.GetValueOrDefault();
        var episodeNumber = item.MappedEpisodeNumber ?? item.EpisodeNumber.GetValueOrDefault();
        var code = $"S{seasonNumber:00}E{episodeNumber:00}";
        return string.IsNullOrWhiteSpace(item.EpisodeTitle) ? code : $"{code}: {item.EpisodeTitle}";
    }

    private string? GetNotificationPoster(SourceItem item)
    {
        if (!string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(item.ProviderId, out var tmdbId))
        {
            return null;
        }

        if (item.MediaKind == MediaKind.Movie)
        {
            var movie = _databaseService.GetTrackedMovieByTmdbId(tmdbId);
            return movie is null
                ? null
                : _posterImageService.GetNotificationHeroImage(MediaKind.Movie, movie.TmdbId, movie.PosterPath);
        }

        var show = _databaseService.GetTrackedShowByTmdbId(tmdbId);
        return show is null
            ? null
            : _posterImageService.GetNotificationHeroImage(MediaKind.TvEpisode, show.TmdbId, show.PosterPath);
    }

    private async Task ProcessHardlinkRemovedAsync(LibraryLinkEventArgs e, CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var result = await Task.Run(() => _symlinkSyncService.RemoveItem(e.Item, e.LinkedPath), cancellationToken);
            if (result.RemovedCount > 0 || result.ErrorCount > 0)
            {
                _logger.Info($"Symlink event removal for {e.Item.FileName}. {result.Summary}", LogTarget.All);
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }
}
