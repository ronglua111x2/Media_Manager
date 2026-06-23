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
    private readonly IAutoTorrentLinkService _autoTorrentLinkService;
    private readonly IWindowsNotificationService _windowsNotificationService;
    private readonly IPosterImageService _posterImageService;
    private readonly AutoTrackCandidatePolicyService _candidatePolicyService;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);
    private readonly SemaphoreSlim _huntLock = new(1, 1);
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);

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
        IAutoTorrentLinkService autoTorrentLinkService,
        IWindowsNotificationService windowsNotificationService,
        IPosterImageService posterImageService,
        AutoTrackCandidatePolicyService candidatePolicyService,
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
        _autoTorrentLinkService = autoTorrentLinkService;
        _windowsNotificationService = windowsNotificationService;
        _posterImageService = posterImageService;
        _candidatePolicyService = candidatePolicyService;
        _logger = logger;
    }

    public bool IsRunning => IsTmdbDiscoveryRunning || IsTorrentHuntRunning || IsReconcileRunning;

    public bool IsTmdbDiscoveryRunning => _discoveryLock.CurrentCount == 0;

    public bool IsTorrentHuntRunning => _huntLock.CurrentCount == 0;

    public bool IsReconcileRunning => _reconcileLock.CurrentCount == 0;

    public async Task<AutoTrackRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var discovery = await RunTmdbDiscoveryAsync(bypassAnchor: true, cancellationToken);
        var hunt = await RunTorrentHuntAsync(cancellationToken);

        var combined = new AutoTrackRunResult
        {
            ShowsProcessed = discovery.ShowsProcessed + hunt.ShowsProcessed,
            EpisodesQueued = hunt.EpisodesQueued,
            CandidatesFound = hunt.CandidatesFound,
            TorrentsAdded = hunt.TorrentsAdded,
            TmdbRefreshed = discovery.TmdbRefreshed,
            Failed = discovery.Failed + hunt.Failed,
            Succeeded = discovery.Succeeded && hunt.Succeeded,
        };
        combined.Summary =
            $"TMDB={discovery.TmdbRefreshed}, Hunt: queued={hunt.EpisodesQueued}, candidates={hunt.CandidatesFound}, added={hunt.TorrentsAdded}, failed={combined.Failed}.";
        PersistRunResult(combined);
        NotifyRunSummary(combined);
        return combined;
    }

    public async Task<AutoTrackRunResult> RunTmdbDiscoveryAsync(bool bypassAnchor = false, CancellationToken cancellationToken = default)
    {
        if (!await _discoveryLock.WaitAsync(0, cancellationToken))
        {
            return SkippedResult("TMDB discovery is already running.");
        }

        try
        {
            return await RunTmdbDiscoveryCoreAsync(bypassAnchor, cancellationToken);
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    public async Task<AutoTrackRunResult> RunTorrentHuntAsync(CancellationToken cancellationToken = default)
    {
        if (!await _huntLock.WaitAsync(0, cancellationToken))
        {
            return SkippedResult("Torrent hunt is already running.");
        }

        try
        {
            return await RunTorrentHuntCoreAsync(cancellationToken);
        }
        finally
        {
            _huntLock.Release();
        }
    }

    public async Task<AutoTrackRunResult> RunBackgroundReconcileAsync(CancellationToken cancellationToken = default)
    {
        if (!await _reconcileLock.WaitAsync(0, cancellationToken))
        {
            return SkippedResult("Background reconcile is already running.");
        }

        try
        {
            return await RunBackgroundReconcileCoreAsync(cancellationToken);
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    private async Task<AutoTrackRunResult> RunTmdbDiscoveryCoreAsync(bool bypassAnchor, CancellationToken cancellationToken)
    {
        var result = new AutoTrackRunResult { Succeeded = true };
        var settings = GetAutoTrackSettings();
        var nowLocal = DateTime.Now;
        var shows = _trackedShowService.GetAutoTrackedShows();
        if (shows.Count == 0)
        {
            result.Summary = "No auto-tracked shows.";
            return result;
        }

        EnsureTmdbDailyCounter(settings, nowLocal);
        var remainingCap = Math.Max(0, settings.MaxTmdbRefreshesPerDay - settings.TmdbRefreshesToday);

        _logger.Info($"Auto-track TMDB discovery started for {shows.Count} show(s). Cap remaining={remainingCap}.", LogTarget.All);

        foreach (var show in shows.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.ShowsProcessed++;

            var currentShow = _databaseService.GetTrackedShow(show.Id) ?? show;
            var episodes = _trackedShowService.GetEpisodes(currentShow.Id);

            if (AutoTrackTmdbEligibility.ShouldResetDormantState(currentShow, settings, nowLocal))
            {
                _databaseService.UpdateTrackedShowAutoTrackTmdbState(
                    currentShow.Id,
                    AutoTrackTmdbState.Active,
                    currentShow.AutoTrackLastTmdbWeekKey);
                currentShow = _databaseService.GetTrackedShow(currentShow.Id) ?? currentShow;
            }

            if (currentShow.SeriesStatus == ShowSeriesStatus.Finished &&
                AutoTrackTmdbEligibility.IsFullyCaughtUp(currentShow, episodes) &&
                currentShow.AutoTrackTmdbState != AutoTrackTmdbState.FinishedComplete)
            {
                _databaseService.UpdateTrackedShowAutoTrackTmdbState(
                    currentShow.Id,
                    AutoTrackTmdbState.FinishedComplete,
                    currentShow.AutoTrackLastTmdbWeekKey);
                continue;
            }

            if (!AutoTrackTmdbEligibility.ShouldRefreshTmdb(currentShow, settings, episodes, nowLocal, bypassAnchor))
            {
                continue;
            }

            if (!bypassAnchor && remainingCap <= 0)
            {
                _logger.Info($"Auto-track TMDB daily cap reached ({settings.MaxTmdbRefreshesPerDay}).", LogTarget.All);
                break;
            }

            try
            {
                var refreshedShow = await _trackedShowService.RefreshShowAsync(currentShow, cancellationToken);
                result.TmdbRefreshed++;
                if (!bypassAnchor)
                {
                    settings.TmdbRefreshesToday++;
                    remainingCap--;
                    _settingsService.Save();
                }

                var refreshedEpisodes = _trackedShowService.GetEpisodes(refreshedShow.Id);
                var weekKey = AutoTrackWeekAnchor.WeekKey(nowLocal);
                var hasPendingLatest = AutoTrackTmdbEligibility.HasPendingLatestEpisode(
                    refreshedShow,
                    refreshedEpisodes,
                    IsAutoTrackHuntBlocked,
                    nowLocal);

                AutoTrackTmdbState nextState;
                if (hasPendingLatest)
                {
                    nextState = AutoTrackTmdbState.Active;
                }
                else if (refreshedShow.SeriesStatus == ShowSeriesStatus.Finished &&
                         AutoTrackTmdbEligibility.IsFullyCaughtUp(refreshedShow, refreshedEpisodes))
                {
                    nextState = AutoTrackTmdbState.FinishedComplete;
                }
                else if (refreshedShow.SeriesStatus == ShowSeriesStatus.Ongoing &&
                         AutoTrackTmdbEligibility.IsFullyCaughtUp(refreshedShow, refreshedEpisodes))
                {
                    nextState = AutoTrackTmdbState.DormantCaughtUp;
                }
                else
                {
                    nextState = AutoTrackTmdbState.Active;
                }

                _databaseService.UpdateTrackedShowAutoTrackTmdbState(refreshedShow.Id, nextState, weekKey);

                if (hasPendingLatest)
                {
                    var latest = AutoTrackTmdbEligibility.FindLatestPendingEpisode(
                        refreshedShow,
                        refreshedEpisodes,
                        IsAutoTrackHuntBlocked,
                        nowLocal);
                    if (latest is not null)
                    {
                        NotifyStage(
                            "Auto-Track",
                            $"{refreshedShow.DisplayTitle} — New episode S{latest.SeasonNumber:00}E{latest.EpisodeNumber:00} detected.",
                            refreshedShow);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Succeeded = false;
                _logger.Warning($"Auto-track TMDB refresh failed for '{currentShow.DisplayTitle}': {ex.Message}", LogTarget.All);
            }
        }

        result.Summary = $"TMDB refreshed={result.TmdbRefreshed}, processed={result.ShowsProcessed}, failed={result.Failed}.";
        _logger.Info($"Auto-track TMDB discovery complete. {result.Summary}", LogTarget.All);
        return result;
    }

    private async Task<AutoTrackRunResult> RunTorrentHuntCoreAsync(CancellationToken cancellationToken)
    {
        var result = new AutoTrackRunResult { Succeeded = true };
        var settings = GetAutoTrackSettings();
        var shows = _trackedShowService.GetAutoTrackedShows();
        if (shows.Count == 0)
        {
            result.Summary = "No auto-tracked shows.";
            return result;
        }

        var huntQueue = BuildHuntQueue(shows);
        if (huntQueue.Count == 0)
        {
            result.Summary = "No pending latest episodes to hunt.";
            return result;
        }

        var batchSize = Math.Clamp(settings.Search.MaxShowsPerHuntCycle, 1, 20);
        var batch = huntQueue.Take(batchSize).ToList();
        var pendingOrdersByShow = new Dictionary<long, (TrackedShow Show, List<TorrentCartOrder> Orders)>();

        foreach (var (show, latestEpisode) in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.ShowsProcessed++;

            if (string.IsNullOrWhiteSpace(show.AutoTrackDownloadFolder))
            {
                NotifyStage("Auto-Track", $"{show.DisplayTitle} — Skipped: assign download folder on Home.", show);
                continue;
            }

            TorrentCartOrder? order = null;
            try
            {
                if (_torrentCartService.TryGetAutoTrackHuntBlockingEpisodeOrder(latestEpisode.Id, out var blockingOrder))
                {
                    if (TryResumeAutoTrackCandidatesFoundOrder(show, blockingOrder, out order))
                    {
                        pendingOrdersByShow[show.Id] = (show, [order]);
                        result.EpisodesQueued++;
                        NotifyStage(
                            "Auto-Track",
                            $"{show.DisplayTitle} — Resuming accept/add for {order.Title}.",
                            show);
                    }

                    continue;
                }

                order = _torrentCartService.PrepareAutoTrackEpisodeOrder(
                    show.Id,
                    latestEpisode.Id,
                    latestEpisode.SeasonNumber,
                    latestEpisode.EpisodeNumber,
                    latestEpisode.Title);
                result.EpisodesQueued++;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("manual cart order", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info(
                    $"Auto-track skipped S{latestEpisode.SeasonNumber:00}E{latestEpisode.EpisodeNumber:00} for '{show.DisplayTitle}': manual cart order in progress.",
                    LogTarget.All);
            }
            catch (Exception ex)
            {
                result.Failed++;
                _logger.Warning(
                    $"Auto-track failed to queue S{latestEpisode.SeasonNumber:00}E{latestEpisode.EpisodeNumber:00} for '{show.DisplayTitle}': {ex.Message}",
                    LogTarget.All);
            }

            if (order is not null)
            {
                pendingOrdersByShow[show.Id] = (show, [order]);
                NotifyStage(
                    "Auto-Track",
                    $"{show.DisplayTitle} — Hunting S{latestEpisode.SeasonNumber:00}E{latestEpisode.EpisodeNumber:00}.",
                    show);
            }
        }

        if (pendingOrdersByShow.Count == 0)
        {
            result.Summary = "No hunt orders queued.";
            return result;
        }

        await RunFetchAndAddPhaseAsync(pendingOrdersByShow, settings, result, cancellationToken);
        result.Succeeded = result.Failed == 0;
        result.Summary =
            $"Hunt: shows={result.ShowsProcessed}, queued={result.EpisodesQueued}, candidates={result.CandidatesFound}, added={result.TorrentsAdded}, failed={result.Failed}.";
        _logger.Info($"Auto-track torrent hunt complete. {result.Summary}", LogTarget.All);
        return result;
    }

    private async Task<AutoTrackRunResult> RunBackgroundReconcileCoreAsync(CancellationToken cancellationToken)
    {
        var result = new AutoTrackRunResult { Succeeded = true };
        var shows = _trackedShowService.GetAutoTrackedShows();
        foreach (var show in shows)
        {
            if (!show.AutoTrackAutoReconcileAndLink)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            result.ShowsProcessed++;

            try
            {
                var episodes = _trackedShowService.GetEpisodes(show.Id);
                var availableBefore = GetAvailableCheckpointEpisodeKeys(show, episodes);

                await _torrentReconciliationService.ReconcileAsync(
                    TorrentReconciliationScope.ForMedia(MediaKind.TvEpisode, show.Id),
                    cancellationToken);
                await LinkReadyAutoTrackEpisodesAsync(show, cancellationToken);

                var sourceItems = _databaseService.GetSourceItems();
                _trackedShowService.RefreshAvailability(show.Id, sourceItems);
                var newlyLinked = GetNewlyAvailableCheckpointEpisodes(
                    show,
                    _trackedShowService.GetEpisodes(show.Id),
                    availableBefore);
                result.LinkedCount += newlyLinked.Count;

                foreach (var episode in newlyLinked)
                {
                    NotifyHardlinkedEpisode(show, episode);
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Succeeded = false;
                _logger.Warning($"Auto-track reconcile failed for '{show.DisplayTitle}': {ex.Message}", LogTarget.All);
            }
        }

        result.Summary = $"Reconcile: shows={result.ShowsProcessed}, linked={result.LinkedCount}.";
        return result;
    }

    private async Task LinkReadyAutoTrackEpisodesAsync(TrackedShow show, CancellationToken cancellationToken)
    {
        if (!show.AutoTrackAutoReconcileAndLink ||
            show.AutoTrackFromSeason is null ||
            show.AutoTrackFromEpisode is null)
        {
            return;
        }

        var fromSeason = show.AutoTrackFromSeason.Value;
        var fromEpisode = show.AutoTrackFromEpisode.Value;

        foreach (var episode in _trackedShowService.GetEpisodes(show.Id))
        {
            if (episode.Availability == EpisodeAvailability.Available ||
                string.IsNullOrWhiteSpace(episode.TorrentHash) ||
                episode.TorrentProgress < 0.999)
            {
                continue;
            }

            if (episode.SeasonNumber < fromSeason ||
                (episode.SeasonNumber == fromSeason && episode.EpisodeNumber < fromEpisode))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _autoTorrentLinkService.LinkEpisodeAsync(
                show.Id,
                episode.SeasonNumber,
                episode.EpisodeNumber,
                cancellationToken);
        }
    }

    private List<(TrackedShow Show, TrackedEpisode Episode)> BuildHuntQueue(IReadOnlyList<TrackedShow> shows)
    {
        var queue = new List<(TrackedShow Show, TrackedEpisode Episode)>();
        foreach (var show in shows.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(show.AutoTrackDownloadFolder))
            {
                continue;
            }

            var episodes = _trackedShowService.GetEpisodes(show.Id);
            var latest = AutoTrackTmdbEligibility.FindLatestPendingEpisode(
                show,
                episodes,
                IsAutoTrackHuntBlocked);
            if (latest is not null)
            {
                queue.Add((show, latest));
            }
        }

        return queue;
    }

    private async Task RunFetchAndAddPhaseAsync(
        Dictionary<long, (TrackedShow Show, List<TorrentCartOrder> Orders)> pendingOrdersByShow,
        AutoTrackSettings settings,
        AutoTrackRunResult result,
        CancellationToken cancellationToken)
    {
        var fetchOptions = new EpisodeFetchOptions
        {
            ForceParallelEpisodeSearch = settings.Search.ForceParallelEpisodeSearch,
            MaxParallelWorkers = settings.Search.MaxParallelWorkersPerShow
        };

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
                var orders = entry.Orders
                    .Select(item => _torrentCartService.GetOrder(item.Id) ?? item)
                    .ToList();
                var ordersNeedingSearch = orders
                    .Where(order => order.Status is TorrentOrderStatus.Draft)
                    .ToList();

                if (ordersNeedingSearch.Count == 0)
                {
                    continue;
                }

                var recipe = _recipeService.GetRecipeOrDefault(show.RecipeId, MediaKind.TvEpisode);
                var episodeIds = ordersNeedingSearch
                    .Where(order => order.EpisodeId is not null)
                    .Select(order => order.EpisodeId!.Value)
                    .Distinct()
                    .ToList();

                foreach (var order in ordersNeedingSearch)
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
                            var order = ordersNeedingSearch.FirstOrDefault(item => item.EpisodeId == episodeId);
                            if (order is not null)
                            {
                                _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Searching, detail);
                            }
                        },
                        cancellationToken,
                        fetchOptions);

                    foreach (var order in ordersNeedingSearch)
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

                        var filtered = _candidatePolicyService.Apply(show, settings, candidates);
                        if (filtered.Count == 0)
                        {
                            result.Failed++;
                            _torrentCartService.UpdateOrderStatus(
                                order.Id,
                                TorrentOrderStatus.NoCandidates,
                                "No candidates passed auto-track quality policy.");
                            NotifyStage(
                                "Auto-Track",
                                $"{show.DisplayTitle} — {order.Title}: no candidates passed quality policy.",
                                show);
                            continue;
                        }

                        result.CandidatesFound++;
                        _torrentCartService.ReplaceCandidates(order.Id, ToCartCandidates(filtered));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Failed += ordersNeedingSearch.Count;
                    foreach (var order in ordersNeedingSearch)
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
            var batchOrderIds = entry.Orders.Select(order => order.Id).ToList();
            _torrentCartService.AcceptSelectedCandidates(MediaKind.TvEpisode, showId, batchOrderIds);

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

    private bool IsAutoTrackHuntBlocked(long episodeId)
    {
        if (_torrentCartService.TryGetAutoTrackHuntBlockingEpisodeOrder(episodeId, out var order) &&
            order is { Source: TorrentOrderSource.AutoTrack, Status: TorrentOrderStatus.CandidatesFound })
        {
            return false;
        }

        return _torrentCartService.TryGetAutoTrackHuntBlockingEpisodeOrder(episodeId, out _) ||
               _torrentCartService.HasActiveManualEpisodeOrder(episodeId);
    }

    private static bool TryResumeAutoTrackCandidatesFoundOrder(
        TrackedShow show,
        TorrentCartOrder? blockingOrder,
        out TorrentCartOrder order)
    {
        order = blockingOrder!;
        if (blockingOrder is null ||
            blockingOrder.Source != TorrentOrderSource.AutoTrack ||
            blockingOrder.Status != TorrentOrderStatus.CandidatesFound ||
            string.IsNullOrWhiteSpace(show.AutoTrackDownloadFolder))
        {
            order = null!;
            return false;
        }

        order = blockingOrder;
        return true;
    }

    private AutoTrackSettings GetAutoTrackSettings()
    {
        _settingsService.Current.AutoTrack ??= new AutoTrackSettings();
        return _settingsService.Current.AutoTrack;
    }

    private static void EnsureTmdbDailyCounter(AutoTrackSettings settings, DateTime nowLocal)
    {
        var dayKey = nowLocal.ToString("yyyy-MM-dd");
        if (!string.Equals(settings.LastTmdbRefreshDayKey, dayKey, StringComparison.Ordinal))
        {
            settings.LastTmdbRefreshDayKey = dayKey;
            settings.TmdbRefreshesToday = 0;
        }
    }

    private static AutoTrackRunResult SkippedResult(string summary)
    {
        return new AutoTrackRunResult
        {
            Succeeded = false,
            Summary = summary
        };
    }

    private async Task AddOrderToClientAsync(TorrentCartOrder order, string savePath, CancellationToken cancellationToken)
    {
        var addedTorrent = await _qbittorrentClient.AddTorrentAsync(
            new AddTorrentRequest
            {
                Url = order.SelectedCandidateUrl,
                PluginName = order.SelectedCandidatePlugin,
                SavePath = savePath,
                Category = _settingsService.Current.AutoTorrent.GetCategoryFor(order.TargetKind),
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

        if (addedTorrent.IsComplete)
        {
            var show = _databaseService.GetTrackedShow(order.MediaId);
            if (show is { IsAutoTracked: true, AutoTrackAutoReconcileAndLink: true })
            {
                var episode = _databaseService.GetTrackedEpisodes(show.Id)
                    .FirstOrDefault(item => item.Id == order.EpisodeId);
                if (episode is not null)
                {
                    var linkResult = await _autoTorrentLinkService.LinkEpisodeAsync(
                        show.Id,
                        episode.SeasonNumber,
                        episode.EpisodeNumber,
                        cancellationToken);
                    if (linkResult.LinkedCount > 0)
                    {
                        _trackedShowService.RefreshAvailability(show.Id);
                        NotifyHardlinkedEpisode(show, episode);
                    }
                }
            }
        }
    }

    public void RecordRunResult(AutoTrackRunResult result)
    {
        PersistRunResult(result);
    }

    private void PersistRunResult(AutoTrackRunResult result)
    {
        var autoTrack = GetAutoTrackSettings();
        autoTrack.LastRunUtc = DateTime.UtcNow;
        autoTrack.LastRunSummary = result.Summary;
        _settingsService.Save();
    }

    private void NotifyStage(string title, string message, TrackedShow show)
    {
        var poster = _posterImageService.GetNotificationHeroImage(
            MediaKind.TvEpisode,
            show.TmdbId,
            show.PosterPath);
        _windowsNotificationService.TryShow(new WindowsNotificationRequest
        {
            Title = title,
            Message = message,
            Tag = null,
            HeroImagePathOrUrl = poster,
            AppLogoOverridePathOrUrl = poster
        });
    }

    private void NotifyHardlinkedEpisode(TrackedShow show, TrackedEpisode episode)
    {
        NotifyStage(
            "Auto-Track",
            $"{show.DisplayTitle} — Hardlinked {FormatEpisodeLabel(episode)}",
            show);
    }

    private static string FormatEpisodeLabel(TrackedEpisode episode)
    {
        var code = $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
        return string.IsNullOrWhiteSpace(episode.Title) ? code : $"{code}: {episode.Title}";
    }

    private static HashSet<(int Season, int Episode)> GetAvailableCheckpointEpisodeKeys(
        TrackedShow show,
        IReadOnlyList<TrackedEpisode> episodes)
    {
        if (show.AutoTrackFromSeason is null || show.AutoTrackFromEpisode is null)
        {
            return [];
        }

        var fromSeason = show.AutoTrackFromSeason.Value;
        var fromEpisode = show.AutoTrackFromEpisode.Value;
        return episodes
            .Where(episode => episode.Availability == EpisodeAvailability.Available)
            .Where(episode =>
                episode.SeasonNumber > fromSeason ||
                (episode.SeasonNumber == fromSeason && episode.EpisodeNumber >= fromEpisode))
            .Select(episode => (episode.SeasonNumber, episode.EpisodeNumber))
            .ToHashSet();
    }

    private static List<TrackedEpisode> GetNewlyAvailableCheckpointEpisodes(
        TrackedShow show,
        IReadOnlyList<TrackedEpisode> episodes,
        HashSet<(int Season, int Episode)> availableBefore)
    {
        if (show.AutoTrackFromSeason is null || show.AutoTrackFromEpisode is null)
        {
            return [];
        }

        var fromSeason = show.AutoTrackFromSeason.Value;
        var fromEpisode = show.AutoTrackFromEpisode.Value;
        return episodes
            .Where(episode => episode.Availability == EpisodeAvailability.Available)
            .Where(episode =>
                episode.SeasonNumber > fromSeason ||
                (episode.SeasonNumber == fromSeason && episode.EpisodeNumber >= fromEpisode))
            .Where(episode => !availableBefore.Contains((episode.SeasonNumber, episode.EpisodeNumber)))
            .ToList();
    }

    private void NotifyRunSummary(AutoTrackRunResult result)
    {
        if (result.EpisodesQueued == 0 &&
            result.TorrentsAdded == 0 &&
            result.LinkedCount == 0 &&
            result.CandidatesFound == 0 &&
            result.TmdbRefreshed == 0)
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
