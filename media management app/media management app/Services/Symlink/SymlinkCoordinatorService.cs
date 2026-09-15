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
        _eventHub.HardlinksCreated += OnHardlinksCreated;
        _eventHub.HardlinksRemoved += OnHardlinksRemoved;
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
            if (result.CreatedCount > 0 || result.RepairedCount > 0 || result.RemovedCount > 0 ||
                result.TouchedSymlinkPaths.Count > 0)
            {
                _eventHub.PublishSymlinkStateChanged();
            }

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
        _eventHub.HardlinksCreated -= OnHardlinksCreated;
        _eventHub.HardlinksRemoved -= OnHardlinksRemoved;

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

    private void OnHardlinksCreated(object? sender, LibraryLinkBatchEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessHardlinksCreatedBatchAsync(e.Items, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error(
                    $"Symlink coordinator failed after pack hardlink created batch ({e.Items.Count} item(s)).",
                    ex,
                    LogTarget.All);
            }
        });
    }

    private void OnHardlinksRemoved(object? sender, LibraryLinkBatchEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessHardlinksRemovedBatchAsync(e.Items, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error(
                    $"Symlink coordinator failed after pack hardlink removed batch ({e.Items.Count} item(s)).",
                    ex,
                    LogTarget.All);
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
                // Event SourceItem is often a different instance than DB-loaded linkedGroup;
                // SyncItem now persists onto it, but prefer TouchedSymlinkPaths as fallback.
                if (string.IsNullOrWhiteSpace(e.Item.SymlinkPath) && result.TouchedSymlinkPaths.Count > 0)
                {
                    e.Item.SymlinkPath = result.TouchedSymlinkPaths[0];
                }

                e.Item.SymlinkPath ??= ResolveSymlinkPath(e.Item);
                _jellyfinLibraryRefreshService.EnqueueFromSourceItem(e.Item);
                _eventHub.PublishSymlinkStateChanged();
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task ProcessHardlinksCreatedBatchAsync(
        IReadOnlyList<LibraryLinkEventArgs> events,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var aggregate = new SymlinkSyncResult();
            var successfulItems = new List<SourceItem>();
            await Task.Run(() =>
            {
                var snapshot = _databaseService.GetSourceItems();
                var groups = _symlinkSyncService.GroupLinkedItemsByPath(snapshot);
                _logger.Info(
                    $"Symlink coordinator created batch: {events.Count} item(s), SourceItems snapshot {snapshot.Count}.",
                    LogTarget.All);

                foreach (var e in events)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var group = ResolveCreatedGroup(e, groups);
                    var result = _symlinkSyncService.SyncItem(e.Item, e.LinkedPath, group);
                    Merge(aggregate, result);
                    TryWriteNfoForItem(e.Item);
                    if (string.IsNullOrWhiteSpace(e.Item.SymlinkPath) && result.TouchedSymlinkPaths.Count > 0)
                    {
                        e.Item.SymlinkPath = result.TouchedSymlinkPaths[0];
                    }

                    e.Item.SymlinkPath ??= ResolveSymlinkPath(e.Item, snapshot);
                    UpdateGroupAfterSync(groups, e, group);
                    if (result.CreatedCount > 0 || result.RepairedCount > 0)
                    {
                        successfulItems.Add(e.Item);
                    }
                }
            }, cancellationToken);

            if (aggregate.CreatedCount > 0 || aggregate.RepairedCount > 0 || aggregate.ErrorCount > 0)
            {
                _logger.Info($"Symlink coordinator created batch finished. {aggregate.Summary}", LogTarget.All);
            }

            if (aggregate.CreatedCount > 0 || aggregate.RepairedCount > 0)
            {
                NotifySymlinkedBatch(successfulItems);
                EnqueueJellyfinForCreatedBatch(events, aggregate);
                _eventHub.PublishSymlinkStateChanged();
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
        ShowSymlinkNotification(item, message);
    }

    private void NotifySymlinkedBatch(IReadOnlyList<SourceItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (items.Count == 1 && items[0].MediaKind == MediaKind.Movie)
        {
            NotifySymlinkedItem(items[0]);
            return;
        }

        var first = items[0];
        var showTitle = first.MatchedTitle ?? first.ShowTitle ?? "Unknown Show";
        var counts = LibraryLinkBatchSummary.CountMembers(
            items.Select(item => (item.IsOrphanPackSpecial, item.MappedSeasonNumber, item.SeasonNumber)));
        var message = LibraryLinkBatchSummary.FormatSymlinkCreated(
            showTitle,
            counts.RegularEpisodes,
            counts.MatchedSpecials,
            counts.OrphanExtras);
        ShowSymlinkNotification(first, message);
    }

    private void ShowSymlinkNotification(SourceItem item, string message)
    {
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

    private void EnqueueJellyfinForCreatedBatch(
        IReadOnlyList<LibraryLinkEventArgs> events,
        SymlinkSyncResult result)
    {
        var tvItem = events.Select(e => e.Item).FirstOrDefault(item => item.MediaKind == MediaKind.TvEpisode);
        if (tvItem is null)
        {
            return;
        }

        if (!string.Equals(tvItem.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(tvItem.ProviderId, out var tmdbId))
        {
            _logger.Debug("Jellyfin refresh skip pack batch: tracked show not resolved.", LogTarget.File | LogTarget.Console);
            return;
        }

        var show = _databaseService.GetTrackedShowByTmdbId(tmdbId);
        if (show is null)
        {
            _logger.Debug("Jellyfin refresh skip pack batch: tracked show not resolved.", LogTarget.File | LogTarget.Console);
            return;
        }

        if (!show.IsAutoTracked)
        {
            _logger.Debug(
                $"Jellyfin refresh skip pack batch: show '{show.DisplayTitle}' is not Auto-Tracked.",
                LogTarget.File | LogTarget.Console);
            return;
        }

        if (show.UsesEpisodeGroup)
        {
            _logger.Info(
                $"Jellyfin refresh skip pack batch: episode-group order '{show.EpisodeGroupName ?? show.EpisodeGroupId}' (NFO-owned).",
                LogTarget.All);
            return;
        }

        var paths = result.TouchedSymlinkPaths
            .Concat(events
                .Select(e => e.Item.SymlinkPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
        {
            return;
        }

        _jellyfinLibraryRefreshService.EnqueuePaths(paths);
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
            var seasonFolders = nfoTargets
                .Select(Path.GetDirectoryName)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var showFolders = nfoTargets
                .Select(GetShowFolderFromEpisodeSymlink)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Delete episode NFOs before symlink removal so empty-folder prune can collapse seasons.
            foreach (var symlinkPath in nfoTargets)
            {
                _nfoWriterService.DeleteEpisodeNfo(symlinkPath);
            }

            var result = await Task.Run(() => _symlinkSyncService.RemoveItem(e.Item, e.LinkedPath), cancellationToken);
            if (result.RemovedCount > 0 || result.ErrorCount > 0)
            {
                _logger.Info($"Symlink event removal for {e.Item.FileName}. {result.Summary}", LogTarget.All);
            }

            if (result.RemovedCount > 0)
            {
                _eventHub.PublishSymlinkStateChanged();
            }

            foreach (var showFolder in showFolders)
            {
                if (!_nfoWriterService.HasRemainingEpisodeArtifacts(showFolder))
                {
                    _nfoWriterService.DeleteTvShowNfo(showFolder);
                }
            }

            foreach (var seasonFolder in seasonFolders)
            {
                _symlinkSyncService.PruneEmptyFolders(seasonFolder);
            }

            foreach (var showFolder in showFolders)
            {
                _symlinkSyncService.PruneEmptyFolders(showFolder);
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task ProcessHardlinksRemovedBatchAsync(
        IReadOnlyList<LibraryLinkEventArgs> events,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var aggregate = new SymlinkSyncResult();
            await Task.Run(() =>
            {
                var snapshot = _databaseService.GetSourceItems();
                _logger.Info(
                    $"Symlink coordinator removed batch: {events.Count} item(s), SourceItems snapshot {snapshot.Count}.",
                    LogTarget.All);

                var nfoTargets = events
                    .SelectMany(e => CollectSymlinkPathsForNfoCleanup(e.Item, snapshot))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var seasonFolders = nfoTargets
                    .Select(Path.GetDirectoryName)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => Path.GetFullPath(path!))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var showFolders = nfoTargets
                    .Select(GetShowFolderFromEpisodeSymlink)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => Path.GetFullPath(path!))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var symlinkPath in nfoTargets)
                {
                    _nfoWriterService.DeleteEpisodeNfo(symlinkPath);
                }

                foreach (var e in events)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var group = ResolveRemovalGroupFromSnapshot(e, snapshot);
                    Merge(aggregate, _symlinkSyncService.RemoveItem(e.Item, e.LinkedPath, group));
                }

                foreach (var showFolder in showFolders)
                {
                    if (!_nfoWriterService.HasRemainingEpisodeArtifacts(showFolder))
                    {
                        _nfoWriterService.DeleteTvShowNfo(showFolder);
                    }
                }

                foreach (var seasonFolder in seasonFolders)
                {
                    _symlinkSyncService.PruneEmptyFolders(seasonFolder);
                }

                foreach (var showFolder in showFolders)
                {
                    _symlinkSyncService.PruneEmptyFolders(showFolder);
                }
            }, cancellationToken);

            if (aggregate.RemovedCount > 0 || aggregate.ErrorCount > 0)
            {
                _logger.Info($"Symlink coordinator removed batch finished. {aggregate.Summary}", LogTarget.All);
            }

            if (aggregate.RemovedCount > 0)
            {
                _eventHub.PublishSymlinkStateChanged();
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
            PruneEmptySeasonFolders(showFolder);
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
                _nfoWriterService.DeleteTvShowNfo(showFolder);
                PruneEmptySeasonFolders(showFolder);
                _symlinkSyncService.PruneEmptyFolders(showFolder);
            }
        }
    }

    private void PruneEmptySeasonFolders(string showFolder)
    {
        if (string.IsNullOrWhiteSpace(showFolder) || !Directory.Exists(showFolder))
        {
            return;
        }

        IEnumerable<string> seasonFolders;
        try
        {
            seasonFolders = Directory.EnumerateDirectories(showFolder).ToList();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to enumerate season folders under {showFolder}: {ex.Message}", LogTarget.All);
            return;
        }

        foreach (var seasonFolder in seasonFolders)
        {
            _symlinkSyncService.PruneEmptyFolders(seasonFolder);
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

    private string? ResolveSymlinkPath(SourceItem item, IReadOnlyList<SourceItem>? sourceItems = null)
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
        return (sourceItems ?? _databaseService.GetSourceItems())
            .Where(candidate => candidate.State == ItemState.Linked)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.LinkedPath))
            .Where(candidate => string.Equals(Path.GetFullPath(candidate.LinkedPath!), normalizedLinkedPath, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.SymlinkPath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }

    private List<string> CollectSymlinkPathsForNfoCleanup(
        SourceItem item,
        IReadOnlyList<SourceItem>? sourceItems = null)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.SymlinkPath))
        {
            paths.Add(item.SymlinkPath);
        }

        var resolvedPath = ResolveSymlinkPath(item, sourceItems);
        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            paths.Add(resolvedPath);
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<SourceItem> ResolveCreatedGroup(
        LibraryLinkEventArgs e,
        Dictionary<string, List<SourceItem>> groups)
    {
        var normalized = Path.GetFullPath(e.LinkedPath);
        if (!groups.TryGetValue(normalized, out var group))
        {
            group = [e.Item];
            groups[normalized] = group;
            return group;
        }

        if (!group.Any(member => member.Id == e.Item.Id && (e.Item.Id != 0 || ReferenceEquals(member, e.Item))))
        {
            if (e.Item.Id == 0 || group.All(member => member.Id != e.Item.Id))
            {
                group.Add(e.Item);
            }
        }

        return group;
    }

    private static void UpdateGroupAfterSync(
        Dictionary<string, List<SourceItem>> groups,
        LibraryLinkEventArgs e,
        List<SourceItem> group)
    {
        var normalized = Path.GetFullPath(e.LinkedPath);
        groups[normalized] = group;
        if (!string.IsNullOrWhiteSpace(e.Item.SymlinkPath))
        {
            foreach (var member in group)
            {
                member.SymlinkPath ??= e.Item.SymlinkPath;
            }
        }
    }

    private static List<SourceItem> ResolveRemovalGroupFromSnapshot(
        LibraryLinkEventArgs e,
        IReadOnlyList<SourceItem> snapshot)
    {
        var group = new List<SourceItem>();
        var seenIds = new HashSet<long>();
        void Add(SourceItem item)
        {
            if (item.Id != 0)
            {
                if (!seenIds.Add(item.Id))
                {
                    return;
                }
            }
            else if (group.Any(member => ReferenceEquals(member, item)))
            {
                return;
            }

            group.Add(item);
        }

        Add(e.Item);
        foreach (var item in snapshot)
        {
            if (string.Equals(item.FilePath, e.Item.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                Add(item);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(e.Item.SymlinkPath) &&
                string.Equals(item.SymlinkPath, e.Item.SymlinkPath, StringComparison.OrdinalIgnoreCase))
            {
                Add(item);
            }
        }

        return group;
    }

    private static void Merge(SymlinkSyncResult aggregate, SymlinkSyncResult itemResult)
    {
        aggregate.CreatedCount += itemResult.CreatedCount;
        aggregate.RepairedCount += itemResult.RepairedCount;
        aggregate.RemovedCount += itemResult.RemovedCount;
        aggregate.SkippedCount += itemResult.SkippedCount;
        aggregate.ErrorCount += itemResult.ErrorCount;
        aggregate.Messages.AddRange(itemResult.Messages);
        foreach (var path in itemResult.TouchedSymlinkPaths)
        {
            if (!aggregate.TouchedSymlinkPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                aggregate.TouchedSymlinkPaths.Add(path);
            }
        }
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
