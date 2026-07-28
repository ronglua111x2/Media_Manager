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
    private readonly INfoWriterService _nfoWriterService;
    private readonly IJellyfinLibraryRefreshService _jellyfinLibraryRefreshService;
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
        INfoWriterService nfoWriterService,
        IJellyfinLibraryRefreshService jellyfinLibraryRefreshService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _symlinkSyncService = symlinkSyncService;
        _eventHub = eventHub;
        _databaseService = databaseService;
        _posterImageService = posterImageService;
        _windowsNotificationService = windowsNotificationService;
        _nfoWriterService = nfoWriterService;
        _jellyfinLibraryRefreshService = jellyfinLibraryRefreshService;
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
            var result = await Task.Run(_symlinkSyncService.ReconcileAll, cancellationToken);
            await Task.Run(WriteAndCleanupEpisodeGroupNfos, cancellationToken);
            await NotifyJellyfinForTouchedPathsAsync(result, cancellationToken);
            return result;
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

            TryWriteNfoForItem(e.Item);

            if (result.CreatedCount > 0 || result.RepairedCount > 0)
            {
                NotifySymlinkedItem(e.Item);
                _jellyfinLibraryRefreshService.EnqueueFromSourceItem(e.Item);
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task NotifyJellyfinForTouchedPathsAsync(SymlinkSyncResult result, CancellationToken cancellationToken)
    {
        if (result.TouchedSymlinkPaths.Count == 0)
        {
            return;
        }

        var itemsBySymlink = _databaseService.GetSourceItems()
            .Where(item => item.State == ItemState.Linked)
            .Where(item => !string.IsNullOrWhiteSpace(item.SymlinkPath))
            .GroupBy(item => Path.GetFullPath(item.SymlinkPath!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var path in result.TouchedSymlinkPaths)
        {
            var normalized = Path.GetFullPath(path);
            if (!itemsBySymlink.TryGetValue(normalized, out var item))
            {
                continue;
            }

            _jellyfinLibraryRefreshService.EnqueueFromSourceItem(item);
        }

        _logger.Info(
            $"Jellyfin refresh after SyncNow: {result.TouchedSymlinkPaths.Count} touched symlink(s), flushing queue.",
            LogTarget.All);

        try
        {
            await _jellyfinLibraryRefreshService.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error("Jellyfin refresh after SyncNow failed.", ex, LogTarget.All);
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
            Kind = NotificationKind.SymlinkCreated,
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
            var nfoTargets = CollectSymlinkPathsForNfoCleanup(e.Item);
            var result = await Task.Run(() => _symlinkSyncService.RemoveItem(e.Item, e.LinkedPath), cancellationToken);
            if (result.RemovedCount > 0 || result.ErrorCount > 0)
            {
                _logger.Info($"Symlink event removal for {e.Item.FileName}. {result.Summary}", LogTarget.All);
            }

            foreach (var symlinkPath in nfoTargets)
            {
                _nfoWriterService.DeleteEpisodeNfo(symlinkPath);
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private void TryWriteNfoForItem(SourceItem item)
    {
        if (item.MediaKind != MediaKind.TvEpisode)
        {
            return;
        }

        if (!TryResolveShowAndEpisode(item, out var show, out var episode) || !show.UsesEpisodeGroup)
        {
            return;
        }

        var symlinkPath = ResolveSymlinkPath(item);
        if (string.IsNullOrWhiteSpace(symlinkPath) || !File.Exists(symlinkPath))
        {
            return;
        }

        _nfoWriterService.WriteEpisodeNfoIfNeeded(symlinkPath, episode, show);
        var showFolder = GetShowFolderFromEpisodeSymlink(symlinkPath);
        if (!string.IsNullOrWhiteSpace(showFolder))
        {
            _nfoWriterService.WriteTvShowNfo(showFolder, show);
        }
    }

    private void WriteAndCleanupEpisodeGroupNfos()
    {
        var settings = _settingsService.Current.Symlink;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.UnifiedRoot))
        {
            return;
        }

        var showsByTmdbId = _databaseService.GetTrackedShows()
            .Where(show => show.UsesEpisodeGroup)
            .ToDictionary(show => show.TmdbId);

        if (showsByTmdbId.Count == 0)
        {
            return;
        }

        var linkedItems = _databaseService.GetSourceItems()
            .Where(item => item.State == ItemState.Linked)
            .Where(item => item.MediaKind == MediaKind.TvEpisode)
            .Where(item => !string.IsNullOrWhiteSpace(item.SymlinkPath))
            .ToList();

        var activePathsByShowFolder = new Dictionary<string, (TrackedShow Show, HashSet<string> Paths)>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in linkedItems)
        {
            if (!TryResolveShowAndEpisode(item, showsByTmdbId, out var show, out var episode))
            {
                continue;
            }

            var symlinkPath = item.SymlinkPath!;
            if (!File.Exists(symlinkPath))
            {
                continue;
            }

            _nfoWriterService.WriteEpisodeNfoIfNeeded(symlinkPath, episode, show);

            var showFolder = GetShowFolderFromEpisodeSymlink(symlinkPath);
            if (string.IsNullOrWhiteSpace(showFolder))
            {
                continue;
            }

            if (!activePathsByShowFolder.TryGetValue(showFolder, out var entry))
            {
                entry = (show, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                activePathsByShowFolder[showFolder] = entry;
            }

            entry.Paths.Add(Path.GetFullPath(symlinkPath));
            _nfoWriterService.WriteTvShowNfo(showFolder, show);
        }

        foreach (var (showFolder, entry) in activePathsByShowFolder)
        {
            _nfoWriterService.CleanupOrphanEpisodeNfos(showFolder, entry.Paths);
        }

        // Clean leftover NFOs for episode-group shows that currently have no active symlinks.
        var showsRoot = Path.Combine(settings.UnifiedRoot, AppConstants.ShowsFolderName);
        if (!Directory.Exists(showsRoot))
        {
            return;
        }

        foreach (var show in showsByTmdbId.Values)
        {
            foreach (var showFolder in FindShowFolders(showsRoot, show.TmdbId))
            {
                if (activePathsByShowFolder.ContainsKey(showFolder))
                {
                    continue;
                }

                _nfoWriterService.CleanupOrphanEpisodeNfos(showFolder, Array.Empty<string>());
            }
        }
    }

    private bool TryResolveShowAndEpisode(SourceItem item, out TrackedShow show, out TrackedEpisode episode)
    {
        show = null!;
        episode = null!;

        if (!string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(item.ProviderId, out var tmdbId))
        {
            return false;
        }

        var trackedShow = _databaseService.GetTrackedShowByTmdbId(tmdbId);
        if (trackedShow is null)
        {
            return false;
        }

        return TryResolveShowAndEpisode(item, new Dictionary<int, TrackedShow> { [tmdbId] = trackedShow }, out show, out episode);
    }

    private bool TryResolveShowAndEpisode(
        SourceItem item,
        IReadOnlyDictionary<int, TrackedShow> showsByTmdbId,
        out TrackedShow show,
        out TrackedEpisode episode)
    {
        show = null!;
        episode = null!;

        if (!string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(item.ProviderId, out var tmdbId) ||
            !showsByTmdbId.TryGetValue(tmdbId, out var trackedShow))
        {
            return false;
        }

        var seasonNumber = item.MappedSeasonNumber ?? item.SeasonNumber;
        var episodeNumber = item.MappedEpisodeNumber ?? item.EpisodeNumber;
        if (seasonNumber is null || episodeNumber is null)
        {
            return false;
        }

        var trackedEpisode = _databaseService.GetTrackedEpisodes(trackedShow.Id)
            .FirstOrDefault(candidate =>
                candidate.SeasonNumber == seasonNumber.Value &&
                candidate.EpisodeNumber == episodeNumber.Value);

        if (trackedEpisode is null)
        {
            return false;
        }

        show = trackedShow;
        episode = trackedEpisode;
        return true;
    }

    private string? ResolveSymlinkPath(SourceItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SymlinkPath))
        {
            return item.SymlinkPath;
        }

        if (string.IsNullOrWhiteSpace(item.LinkedPath))
        {
            return null;
        }

        var normalizedLinkedPath = Path.GetFullPath(item.LinkedPath);
        return _databaseService.GetSourceItems()
            .Where(candidate => candidate.State == ItemState.Linked)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.LinkedPath))
            .Where(candidate => string.Equals(Path.GetFullPath(candidate.LinkedPath!), normalizedLinkedPath, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.SymlinkPath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }

    private static List<string> CollectSymlinkPathsForNfoCleanup(SourceItem item)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.SymlinkPath))
        {
            paths.Add(item.SymlinkPath);
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? GetShowFolderFromEpisodeSymlink(string symlinkPath)
    {
        var seasonFolder = Path.GetDirectoryName(symlinkPath);
        return string.IsNullOrWhiteSpace(seasonFolder) ? null : Path.GetDirectoryName(seasonFolder);
    }

    private static IEnumerable<string> FindShowFolders(string showsRoot, int tmdbId)
    {
        var marker = $"[tmdbid-{tmdbId}]";
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(showsRoot);
        }
        catch
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            if (Path.GetFileName(directory).Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.GetFullPath(directory);
            }
        }
    }
}
