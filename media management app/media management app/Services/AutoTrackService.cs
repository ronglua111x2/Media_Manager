using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class AutoTrackService : IAutoTrackService
{
    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _databaseService;
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITorrentCartService _torrentCartService;
    private readonly IFetchJobService _fetchJobService;
    private readonly IRecipeService _recipeService;
    private readonly IWarpCliService _warpCliService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly ITorrentReconciliationService _torrentReconciliationService;
    private readonly IWindowsNotificationService _windowsNotificationService;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public AutoTrackService(
        ISettingsService settingsService,
        IDatabaseService databaseService,
        ITrackedShowService trackedShowService,
        ITorrentCartService torrentCartService,
        IFetchJobService fetchJobService,
        IRecipeService recipeService,
        IWarpCliService warpCliService,
        IQbittorrentClient qbittorrentClient,
        ITorrentReconciliationService torrentReconciliationService,
        IWindowsNotificationService windowsNotificationService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;
        _trackedShowService = trackedShowService;
        _torrentCartService = torrentCartService;
        _fetchJobService = fetchJobService;
        _recipeService = recipeService;
        _warpCliService = warpCliService;
        _qbittorrentClient = qbittorrentClient;
        _torrentReconciliationService = torrentReconciliationService;
        _windowsNotificationService = windowsNotificationService;
        _logger = logger;
    }

    public bool IsRunning => _runLock.CurrentCount == 0;

    public async Task<AutoTrackRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            return new AutoTrackRunResult
            {
                Succeeded = false,
                Summary = "Auto-track is already running."
            };
        }

        try
        {
            return await RunCoreAsync(cancellationToken);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<AutoTrackRunResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        var result = new AutoTrackRunResult();
        var shows = _trackedShowService.GetAutoTrackedShows();
        if (shows.Count == 0)
        {
            result.Succeeded = true;
            result.Summary = "No auto-tracked shows.";
            PersistRunResult(result);
            return result;
        }

        _logger.Info($"Auto-track run started for {shows.Count} show(s).", LogTarget.All);

        var pendingOrdersByShow = new Dictionary<long, (TrackedShow Show, List<TorrentCartOrder> Orders)>();

        // Phase A prep: discover new episodes and queue cart orders
        foreach (var show in shows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.ShowsProcessed++;

            var refreshedShow = show;
            if (show.SeriesStatus == ShowSeriesStatus.Ongoing)
            {
                try
                {
                    refreshedShow = await _trackedShowService.RefreshShowAsync(show, cancellationToken);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    _logger.Warning($"Auto-track TMDB refresh failed for '{show.DisplayTitle}': {ex.Message}", LogTarget.All);
                }
            }

            if (!refreshedShow.IsAutoTracked)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(refreshedShow.AutoTrackDownloadFolder))
            {
                NotifyStage("Auto-Track", $"{refreshedShow.DisplayTitle} — Skipped: assign download folder on Home.", refreshedShow);
                continue;
            }

            var episodes = FindPendingEpisodes(refreshedShow);
            if (episodes.Count == 0)
            {
                continue;
            }

            NotifyStage(
                "Auto-Track",
                $"{refreshedShow.DisplayTitle} — Found {episodes.Count} new episode(s) from {refreshedShow.AutoTrackCheckpointLabel}.",
                refreshedShow);

            var orders = new List<TorrentCartOrder>();
            foreach (var episode in episodes)
            {
                try
                {
                    var order = _torrentCartService.AddEpisodeOrder(
                        refreshedShow.Id,
                        episode.Id,
                        episode.SeasonNumber,
                        episode.EpisodeNumber,
                        episode.Title);
                    orders.Add(order);
                    result.EpisodesQueued++;
                }
                catch (InvalidOperationException)
                {
                    // Episode already has an active cart order.
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    _logger.Warning(
                        $"Auto-track failed to queue S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00} for '{refreshedShow.DisplayTitle}': {ex.Message}",
                        LogTarget.All);
                }
            }

            if (orders.Count > 0)
            {
                pendingOrdersByShow[refreshedShow.Id] = (refreshedShow, orders);
            }
        }

        // Phase A: fetch, accept, add torrents
        if (pendingOrdersByShow.Count > 0)
        {
            await RunFetchAndAddPhaseAsync(pendingOrdersByShow, result, cancellationToken);
        }

        // Phase B: reconcile per show (always, even when no new episodes)
        await RunReconcilePhaseAsync(shows, result, cancellationToken);

        result.Succeeded = result.Failed == 0 ||
                           result.TorrentsAdded > 0 ||
                           result.CandidatesFound > 0 ||
                           result.LinkedCount > 0;
        result.Summary =
            $"Shows={result.ShowsProcessed}, Queued={result.EpisodesQueued}, Candidates={result.CandidatesFound}, Added={result.TorrentsAdded}, Reconciled={result.ReconciledCount}, Linked={result.LinkedCount}, Failed={result.Failed}.";
        _logger.Info($"Auto-track run complete. {result.Summary}", LogTarget.All);
        PersistRunResult(result);
        NotifyRunSummary(result);
        return result;
    }

    private async Task RunFetchAndAddPhaseAsync(
        Dictionary<long, (TrackedShow Show, List<TorrentCartOrder> Orders)> pendingOrdersByShow,
        AutoTrackRunResult result,
        CancellationToken cancellationToken)
    {
        var warpConnected = false;
        var warpEnabled = _settingsService.Current.Warp?.Enabled ?? true;
        if (warpEnabled && _warpCliService.IsAvailable)
        {
            var timeout = TimeSpan.FromSeconds(_settingsService.Current.Warp?.ConnectTimeoutSeconds ?? 30);
            warpConnected = await _warpCliService.ConnectAsync(timeout, cancellationToken);
            if (!warpConnected)
            {
                _logger.Warning("Auto-track continuing without WARP (connect failed or timed out).", LogTarget.All);
            }
        }

        try
        {
            foreach (var (showId, entry) in pendingOrdersByShow)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var show = _databaseService.GetTrackedShow(showId) ?? entry.Show;
                var orders = entry.Orders;
                var recipe = _recipeService.GetRecipeOrDefault(show.RecipeId, MediaKind.TvEpisode);
                var episodeIds = orders
                    .Where(order => order.EpisodeId is not null)
                    .Select(order => order.EpisodeId!.Value)
                    .Distinct()
                    .ToList();

                foreach (var order in orders)
                {
                    _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Searching, $"Auto-track search with recipe: {recipe.Name}");
                }

                try
                {
                    var candidatesByEpisodeId = await _fetchJobService.FetchEpisodeCandidatesAsync(
                        show.Id,
                        episodeIds,
                        recipe.RecipeId,
                        (episodeId, detail) =>
                        {
                            var order = orders.FirstOrDefault(item => item.EpisodeId == episodeId);
                            if (order is not null)
                            {
                                _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Searching, detail);
                            }
                        },
                        cancellationToken);

                    foreach (var order in orders)
                    {
                        if (order.EpisodeId is null)
                        {
                            continue;
                        }

                        if (!candidatesByEpisodeId.TryGetValue(order.EpisodeId.Value, out var candidates) ||
                            candidates.Count == 0)
                        {
                            result.Failed++;
                            _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.NoCandidates, "No candidates found.");
                            continue;
                        }

                        result.CandidatesFound++;
                        _torrentCartService.ReplaceCandidates(order.Id, ToCartCandidates(candidates));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Failed += orders.Count;
                    foreach (var order in orders)
                    {
                        _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Failed, ex.Message);
                    }

                    _logger.Warning($"Auto-track search failed for '{show.DisplayTitle}': {ex.Message}", LogTarget.All);
                }
            }
        }
        finally
        {
            if (warpConnected)
            {
                await _warpCliService.DisconnectAsync(cancellationToken);
            }
        }

        foreach (var (showId, entry) in pendingOrdersByShow)
        {
            var show = _databaseService.GetTrackedShow(showId) ?? entry.Show;
            _torrentCartService.AcceptSelectedCandidates(MediaKind.TvEpisode, showId);

            foreach (var queued in entry.Orders)
            {
                var approved = _torrentCartService.GetOrder(queued.Id);
                if (approved is null || approved.Status != TorrentOrderStatus.Approved)
                {
                    continue;
                }

                NotifyStage(
                    "Auto-Track",
                    $"{show.DisplayTitle} — {approved.Title} candidate: {approved.SelectedCandidateName}",
                    show);
            }
        }

        foreach (var (showId, entry) in pendingOrdersByShow)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var show = _databaseService.GetTrackedShow(showId) ?? entry.Show;
            var savePath = show.AutoTrackDownloadFolder?.Trim();
            if (string.IsNullOrWhiteSpace(savePath))
            {
                continue;
            }

            var approvedOrders = _torrentCartService.GetOrders(MediaKind.TvEpisode, showId)
                .Where(order => entry.Orders.Any(queued => queued.Id == order.Id))
                .Where(order => order.Status == TorrentOrderStatus.Approved && order.HasSelectedCandidate)
                .ToList();

            foreach (var seasonNumber in approvedOrders
                         .Where(order => order.SeasonNumber is not null)
                         .Select(order => order.SeasonNumber!.Value)
                         .Distinct())
            {
                _trackedShowService.UpdateSeasonDownloadFolder(showId, seasonNumber, savePath);
            }

            foreach (var order in approvedOrders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await AddOrderToClientAsync(order, savePath, cancellationToken);
                    result.TorrentsAdded++;
                    NotifyStage(
                        "Auto-Track",
                        $"{show.DisplayTitle} — Added {order.Title} → {savePath}",
                        show);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Failed, ex.Message);
                    _logger.Warning($"Auto-track add failed for '{order.Title}': {ex.Message}", LogTarget.All);
                }
            }
        }
    }

    private async Task RunReconcilePhaseAsync(
        IReadOnlyList<TrackedShow> shows,
        AutoTrackRunResult result,
        CancellationToken cancellationToken)
    {
        foreach (var show in shows)
        {
            if (!show.AutoTrackAutoReconcileAndLink)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var reconcileResult = await _torrentReconciliationService.ReconcileAsync(
                    TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, show.Id),
                    cancellationToken);
                result.ReconciledCount += reconcileResult.MatchedCount;
                result.LinkedCount += reconcileResult.LinkedCount;

                if (reconcileResult.LinkedCount > 0)
                {
                    NotifyStage(
                        "Auto-Track",
                        $"{show.DisplayTitle} — Hardlinked {reconcileResult.LinkedCount} episode(s).",
                        show);
                }
                else if (reconcileResult.MatchedCount > 0)
                {
                    NotifyStage(
                        "Auto-Track",
                        $"{show.DisplayTitle} — Reconciled (matched={reconcileResult.MatchedCount}, downloading).",
                        show);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Auto-track reconcile failed for '{show.DisplayTitle}': {ex.Message}", LogTarget.All);
            }
        }

        _trackedShowService.RefreshAvailability();
    }

    private List<TrackedEpisode> FindPendingEpisodes(TrackedShow show)
    {
        if (!show.IsAutoTracked)
        {
            return [];
        }

        var fromSeason = show.AutoTrackFromSeason!.Value;
        var fromEpisode = show.AutoTrackFromEpisode!.Value;

        return _trackedShowService.GetEpisodes(show.Id)
            .Where(episode => IsAtOrAfterCheckpoint(episode, fromSeason, fromEpisode))
            .Where(episode => episode.Availability == EpisodeAvailability.Missing)
            .Where(episode => string.IsNullOrWhiteSpace(episode.TorrentHash))
            .Where(episode => !_torrentCartService.TryGetActiveEpisodeOrder(episode.Id, out _))
            .OrderBy(episode => episode.SeasonNumber)
            .ThenBy(episode => episode.EpisodeNumber)
            .ToList();
    }

    private static bool IsAtOrAfterCheckpoint(TrackedEpisode episode, int fromSeason, int fromEpisode)
    {
        return episode.SeasonNumber > fromSeason ||
               (episode.SeasonNumber == fromSeason && episode.EpisodeNumber >= fromEpisode);
    }

    private async Task AddOrderToClientAsync(TorrentCartOrder order, string savePath, CancellationToken cancellationToken)
    {
        var addedTorrent = await _qbittorrentClient.AddTorrentAsync(
            new AddTorrentRequest
            {
                Url = order.SelectedCandidateUrl,
                PluginName = order.SelectedCandidatePlugin,
                SavePath = savePath,
                Category = FirstNonEmpty(_settingsService.Current.AutoTorrent.CategoryName, "AutoTorrent"),
                Tags = "media-manager",
                Paused = false
            },
            cancellationToken);

        if (order.EpisodeId is null)
        {
            throw new InvalidOperationException("Episode order is missing episode id.");
        }

        var candidate = ToEpisodeCandidate(order);
        _trackedShowService.UpdateSelectedCandidate(order.EpisodeId.Value, candidate);
        _trackedShowService.UpdateTorrentState(order.EpisodeId.Value, addedTorrent);

        order.TorrentHash = addedTorrent.Hash;
        order.TorrentName = addedTorrent.Name;
        order.TorrentState = addedTorrent.State;
        order.TorrentProgress = addedTorrent.Progress;
        order.Status = addedTorrent.IsComplete ? TorrentOrderStatus.Completed : TorrentOrderStatus.Downloading;
        order.StatusDetail = addedTorrent.IsComplete
            ? $"Ready to link: {addedTorrent.Name}"
            : $"Downloading: {addedTorrent.Name} ({addedTorrent.ProgressDisplay})";
        _torrentCartService.SaveOrder(order);
    }

    private void PersistRunResult(AutoTrackRunResult result)
    {
        _settingsService.Current.AutoTrack ??= new AutoTrackSettings();
        _settingsService.Current.AutoTrack.LastRunUtc = DateTime.UtcNow;
        _settingsService.Current.AutoTrack.LastRunSummary = result.Summary;
        _settingsService.Save();
    }

    private void NotifyStage(string title, string message, TrackedShow show)
    {
        _windowsNotificationService.TryShow(new WindowsNotificationRequest
        {
            Title = title,
            Message = message,
            Tag = null,
            HeroImagePathOrUrl = GetPosterHeroUrl(show.PosterPath)
        });
    }

    private void NotifyRunSummary(AutoTrackRunResult result)
    {
        if (result.EpisodesQueued == 0 &&
            result.TorrentsAdded == 0 &&
            result.LinkedCount == 0 &&
            result.CandidatesFound == 0)
        {
            return;
        }

        _windowsNotificationService.TryShow(new WindowsNotificationRequest
        {
            Title = "Auto-Track",
            Message = result.Summary,
            Tag = null
        });
    }

    private static string? GetPosterHeroUrl(string? posterPath)
    {
        return string.IsNullOrWhiteSpace(posterPath)
            ? null
            : $"https://image.tmdb.org/t/p/w342{posterPath}";
    }

    private static IReadOnlyList<TorrentCartOrderCandidate> ToCartCandidates(IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        return candidates.Select((candidate, index) => new TorrentCartOrderCandidate
        {
            Rank = index + 1,
            IsSelected = index == 0,
            Name = candidate.FileName,
            Url = candidate.FileUrl,
            PluginName = candidate.PluginName,
            FileSize = candidate.FileSize,
            Seeders = candidate.Seeders,
            Leechers = candidate.Leechers,
            Quality = candidate.QualityLabel,
            AudioCodec = candidate.AudioCodecLabel,
            CoveredSeasons = string.Empty,
            TotalScore = candidate.TotalScore
        }).ToList();
    }

    private static EpisodeFetchCandidate ToEpisodeCandidate(TorrentCartOrder order)
    {
        return new EpisodeFetchCandidate
        {
            EpisodeId = order.EpisodeId ?? 0,
            FileName = order.SelectedCandidateName,
            FileUrl = order.SelectedCandidateUrl,
            FileSize = order.SelectedCandidateFileSize,
            Seeders = order.SelectedCandidateSeeders,
            Leechers = order.SelectedCandidateLeechers,
            PluginName = order.SelectedCandidatePlugin,
            QualityLabel = order.SelectedCandidateQuality,
            AudioCodecLabel = order.SelectedCandidateAudioCodec,
            TotalScore = order.SelectedCandidateTotalScore
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}
