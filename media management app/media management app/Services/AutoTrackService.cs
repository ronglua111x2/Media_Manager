using System.IO;
using System.Net.Http;
using System.Text.Json;
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
        // TMDB discovery (bypass weekly anchor) then hunts eligible pending with schedule bypass.
        var result = await RunTmdbDiscoveryAsync(bypassAnchor: true, cancellationToken);
        PersistRunResult(result);
        NotifyRunSummary(result);
        return result;
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

    public async Task<AutoTrackRunResult> RunTorrentHuntAsync(
        CancellationToken cancellationToken = default,
        bool resumeOnly = false,
        bool bypassSchedule = false)
    {
        if (!await _huntLock.WaitAsync(0, cancellationToken))
        {
            return SkippedResult("Torrent hunt is already running.");
        }

        try
        {
            return await RunTorrentHuntCoreAsync(cancellationToken, resumeOnly, bypassSchedule);
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

        var budget = settings.DailyBudget ??= new TmdbDailyBudget();
        var maxPerDay = settings.MaxTmdbRefreshesPerDay;
        var remainingCap = budget.Remaining(maxPerDay, nowLocal);
        var budgetMutated = false;
        var enforceDailyBudget = !bypassAnchor;
        var pendingHuntCount = BuildHuntQueue(shows, bypassSchedule: bypassAnchor, resumeOnly: false).Count;

        if (bypassAnchor)
        {
            _logger.Info(
                $"Auto-track TMDB discovery started for {shows.Count} show(s). Manual Run Now — daily budget not applied (remaining would be {remainingCap}/{maxPerDay}).",
                LogTarget.All);
        }
        else if (remainingCap == 0 && pendingHuntCount == 0)
        {
            result.Summary = $"Daily cap reached ({maxPerDay}/day); no pending episodes to hunt.";
            _logger.Debug(result.Summary, LogTarget.File | LogTarget.Console);
            return result;
        }
        else if (remainingCap == 0)
        {
            _logger.Info(
                $"TMDB daily cap already exhausted ({maxPerDay}/day). Skipping refreshes; hunting {pendingHuntCount} pending episode(s).",
                LogTarget.All);
        }
        else
        {
            _logger.Info(
                $"Auto-track TMDB discovery started for {shows.Count} show(s). Cap remaining={remainingCap}/{maxPerDay}.",
                LogTarget.All);
        }

        var warpOwnedByUs = false;
        var warpSettings = _settingsService.Current.Warp;
        var autoRecoverOnSsl = (warpSettings?.Enabled ?? true) &&
                               (warpSettings?.AutoRecoverOnSsl ?? true) &&
                               _warpCliService.IsAvailable;

        try
        {
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
                        currentShow.AutoTrackLastTmdbWeekKey,
                        currentShow.AutoTrackLastTmdbRefreshLocal);
                    currentShow = _databaseService.GetTrackedShow(currentShow.Id) ?? currentShow;
                }

                if (currentShow.SeriesStatus == ShowSeriesStatus.Finished &&
                    AutoTrackTmdbEligibility.IsFullyCaughtUp(currentShow, episodes) &&
                    currentShow.AutoTrackTmdbState != AutoTrackTmdbState.FinishedComplete)
                {
                    _databaseService.UpdateTrackedShowAutoTrackTmdbState(
                        currentShow.Id,
                        AutoTrackTmdbState.FinishedComplete,
                        currentShow.AutoTrackLastTmdbWeekKey,
                        currentShow.AutoTrackLastTmdbRefreshLocal);
                    continue;
                }

                if (!AutoTrackTmdbEligibility.ShouldRefreshTmdb(currentShow, settings, episodes, nowLocal, bypassAnchor))
                {
                    continue;
                }

                if (enforceDailyBudget && remainingCap <= 0)
                {
                    _logger.Info(
                        $"Auto-track TMDB daily cap reached ({budget.Used}/{maxPerDay} used). Stopping further refreshes.",
                        LogTarget.All);
                    break;
                }

                try
                {
                    var refreshedShow = await RefreshShowWithSslWarpRecoveryAsync(
                        currentShow,
                        autoRecoverOnSsl,
                        () => warpOwnedByUs,
                        owned => warpOwnedByUs = owned,
                        cancellationToken);

                    result.TmdbRefreshed++;
                    if (enforceDailyBudget)
                    {
                        if (!budget.TryConsume(maxPerDay, nowLocal))
                        {
                            _logger.Warning(
                                $"TMDB budget consume failed after successful refresh (used={budget.Used}/{maxPerDay}).",
                                LogTarget.All);
                        }

                        remainingCap = budget.Remaining(maxPerDay, nowLocal);
                        budgetMutated = true;
                        _logger.Info(
                            $"TMDB refresh ok for '{refreshedShow.DisplayTitle}'. Budget {budget.Used}/{maxPerDay} used, remaining={remainingCap}.",
                            LogTarget.File | LogTarget.Console);
                    }
                    else
                    {
                        _logger.Info(
                            $"TMDB refresh ok for '{refreshedShow.DisplayTitle}' (manual Run Now — budget unchanged).",
                            LogTarget.File | LogTarget.Console);
                    }

                    var refreshedEpisodes = _trackedShowService.GetEpisodes(refreshedShow.Id);
                    var pastAnchor = AutoTrackWeekAnchor.IsPastAnchorThisWeek(refreshedShow, nowLocal, settings);
                    var hasPendingLatest = AutoTrackTmdbEligibility.HasPendingLatestEpisode(
                        refreshedShow,
                        refreshedEpisodes,
                        IsAutoTrackHuntBlocked,
                        nowLocal);

                    AutoTrackTmdbState nextState;
                    string? weekKeyToStore = refreshedShow.AutoTrackLastTmdbWeekKey;
                    if (hasPendingLatest)
                    {
                        nextState = AutoTrackTmdbState.Active;
                        if (pastAnchor)
                        {
                            weekKeyToStore = AutoTrackWeekAnchor.WeekKey(nowLocal);
                        }
                    }
                    else if (refreshedShow.SeriesStatus == ShowSeriesStatus.Finished &&
                             AutoTrackTmdbEligibility.IsFullyCaughtUp(refreshedShow, refreshedEpisodes))
                    {
                        nextState = AutoTrackTmdbState.FinishedComplete;
                        if (pastAnchor)
                        {
                            weekKeyToStore = AutoTrackWeekAnchor.WeekKey(nowLocal);
                        }
                    }
                    else if (refreshedShow.SeriesStatus == ShowSeriesStatus.Ongoing &&
                             AutoTrackTmdbEligibility.IsFullyCaughtUp(refreshedShow, refreshedEpisodes))
                    {
                        // Pre-anchor Run Now must not mark dormant / satisfy this week's schedule.
                        if (pastAnchor)
                        {
                            nextState = AutoTrackTmdbState.DormantCaughtUp;
                            weekKeyToStore = AutoTrackWeekAnchor.WeekKey(nowLocal);
                        }
                        else
                        {
                            nextState = AutoTrackTmdbState.Active;
                        }
                    }
                    else
                    {
                        nextState = AutoTrackTmdbState.Active;
                        if (pastAnchor)
                        {
                            weekKeyToStore = AutoTrackWeekAnchor.WeekKey(nowLocal);
                        }
                    }

                    _databaseService.UpdateTrackedShowAutoTrackTmdbState(
                        refreshedShow.Id,
                        nextState,
                        weekKeyToStore,
                        nowLocal);

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
                                refreshedShow,
                                NotificationKind.AutoTrackNewEpisode);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Succeeded = false;
                    _logger.Warning($"Auto-track TMDB refresh failed for '{currentShow.DisplayTitle}': {ex.Message}", LogTarget.All);
                }
            }

            // New hunts run after TMDB checks: schedule-eligible pending episodes (or all if bypassAnchor).
            var hunt = await RunTorrentHuntAsync(
                cancellationToken,
                resumeOnly: false,
                bypassSchedule: bypassAnchor);
            MergeHuntIntoDiscoveryResult(result, hunt);
        }
        finally
        {
            if (warpOwnedByUs)
            {
                await _warpCliService.DisconnectAsync(CancellationToken.None);
            }
        }

        result.Summary =
            $"TMDB refreshed={result.TmdbRefreshed}, hunt queued={result.EpisodesQueued}, candidates={result.CandidatesFound}, added={result.TorrentsAdded}, failed={result.Failed}.";
        if (budgetMutated)
        {
            try
            {
                _settingsService.Save();
            }
            catch (IOException ex)
            {
                _logger.Warning($"Failed to persist TMDB daily budget after discovery: {ex.Message}. Retrying once.", LogTarget.All);
                try
                {
                    _settingsService.Save();
                }
                catch (Exception retryEx)
                {
                    _logger.Warning($"TMDB daily budget persist retry failed: {retryEx.Message}", LogTarget.All);
                }
            }
        }

        if (result.TmdbRefreshed > 0 ||
            result.EpisodesQueued > 0 ||
            result.CandidatesFound > 0 ||
            result.TorrentsAdded > 0 ||
            result.Failed > 0)
        {
            _logger.Info($"Auto-track TMDB discovery complete. {result.Summary}", LogTarget.All);
        }
        else
        {
            _logger.Debug($"Auto-track TMDB discovery complete. {result.Summary}", LogTarget.File | LogTarget.Console);
        }

        return result;
    }

    private async Task<TrackedShow> RefreshShowWithSslWarpRecoveryAsync(
        TrackedShow show,
        bool autoRecoverOnSsl,
        Func<bool> getWarpOwned,
        Action<bool> setWarpOwned,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _trackedShowService.RefreshShowAsync(show, cancellationToken);
        }
        catch (Exception ex) when (autoRecoverOnSsl && SslTlsErrorDetector.IsSslOrTlsError(ex))
        {
            _logger.Warning(
                $"TMDB SSL/TLS error for '{show.DisplayTitle}': {ex.Message}. Attempting WARP self-recover.",
                LogTarget.All);

            if (!getWarpOwned())
            {
                var timeout = TimeSpan.FromSeconds(_settingsService.Current.Warp?.ConnectTimeoutSeconds ?? 30);
                var attempt = await _warpCliService.ConnectOwnedAsync(timeout, cancellationToken);
                if (!attempt.Connected)
                {
                    _logger.Warning(
                        "WARP connect for SSL self-recover failed or timed out. Re-throwing original TMDB error.",
                        LogTarget.All);
                    throw;
                }

                if (attempt.Owned)
                {
                    setWarpOwned(true);
                }
                else
                {
                    _logger.Info(
                        "WARP already connected; Auto-Track SSL recover will leave the existing session open.",
                        LogTarget.File | LogTarget.Console);
                }
            }

            _logger.Info($"Retrying TMDB refresh for '{show.DisplayTitle}' with WARP.", LogTarget.All);
            return await _trackedShowService.RefreshShowAsync(show, cancellationToken);
        }
    }

    private static void MergeHuntIntoDiscoveryResult(AutoTrackRunResult discovery, AutoTrackRunResult hunt)
    {
        if (hunt.Summary?.Contains("already running", StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        discovery.EpisodesQueued += hunt.EpisodesQueued;
        discovery.CandidatesFound += hunt.CandidatesFound;
        discovery.TorrentsAdded += hunt.TorrentsAdded;
        discovery.Failed += hunt.Failed;
        discovery.Succeeded = discovery.Succeeded && hunt.Succeeded;
        discovery.ShowsProcessed += hunt.ShowsProcessed;
    }

    private async Task<AutoTrackRunResult> RunTorrentHuntCoreAsync(
        CancellationToken cancellationToken,
        bool resumeOnly,
        bool bypassSchedule)
    {
        var result = new AutoTrackRunResult { Succeeded = true };
        var settings = GetAutoTrackSettings();
        var shows = _trackedShowService.GetAutoTrackedShows();
        if (shows.Count == 0)
        {
            result.Summary = "No auto-tracked shows.";
            return result;
        }

        var huntQueue = BuildHuntQueue(shows, bypassSchedule, resumeOnly);
        if (huntQueue.Count == 0)
        {
            result.Summary = resumeOnly
                ? "No CandidatesFound orders to resume."
                : "No pending episodes to hunt.";
            return result;
        }

        var batchSize = Math.Clamp(settings.Search.MaxShowsPerHuntCycle, 1, 20);
        var batch = huntQueue.Take(batchSize).ToList();
        var pendingOrdersByShow = new Dictionary<long, (TrackedShow Show, List<TorrentCartOrder> Orders)>();

        foreach (var (show, episodesToHunt) in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.ShowsProcessed++;

            if (string.IsNullOrWhiteSpace(show.AutoTrackDownloadFolder))
            {
                NotifyStage(
                    "Auto-Track",
                    $"{show.DisplayTitle} — Skipped: assign download folder on Home.",
                    show,
                    NotificationKind.AutoTrackHuntProgress);
                continue;
            }

            var orders = new List<TorrentCartOrder>();
            foreach (var episode in episodesToHunt)
            {
                TorrentCartOrder? order = null;
                try
                {
                    if (_torrentCartService.TryGetAutoTrackHuntBlockingEpisodeOrder(episode.Id, out var blockingOrder))
                    {
                        if (TryResumeAutoTrackCandidatesFoundOrder(show, blockingOrder, out order))
                        {
                            orders.Add(order);
                            result.EpisodesQueued++;
                            NotifyStage(
                                "Auto-Track",
                                $"{show.DisplayTitle} — Resuming accept/add for {order.Title}.",
                                show,
                                NotificationKind.AutoTrackHuntProgress);
                        }

                        continue;
                    }

                    if (resumeOnly)
                    {
                        continue;
                    }

                    order = _torrentCartService.PrepareAutoTrackEpisodeOrder(
                        show.Id,
                        episode.Id,
                        episode.SeasonNumber,
                        episode.EpisodeNumber,
                        episode.Title);
                    orders.Add(order);
                    result.EpisodesQueued++;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("manual cart order", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info(
                        $"Auto-track skipped S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00} for '{show.DisplayTitle}': manual cart order in progress.",
                        LogTarget.All);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    _logger.Warning(
                        $"Auto-track failed to queue S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00} for '{show.DisplayTitle}': {ex.Message}",
                        LogTarget.All);
                }
            }

            if (orders.Count > 0)
            {
                pendingOrdersByShow[show.Id] = (show, orders);
                NotifyStage(
                    "Auto-Track",
                    $"{show.DisplayTitle} — {FormatHuntBatchStage(episodesToHunt)}.",
                    show,
                    NotificationKind.AutoTrackHuntProgress);
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
        var pendingShowIds = GetPendingAutoTrackReconcileShowIds();
        if (pendingShowIds.Count == 0)
        {
            result.Summary = "Reconcile skipped: no pending download queue.";
            _logger.Info(result.Summary, LogTarget.File | LogTarget.Console);
            return result;
        }

        var shows = _trackedShowService.GetAutoTrackedShows()
            .Where(show => pendingShowIds.Contains(show.Id))
            .ToList();

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

    public bool HasPendingAutoTrackDownloadQueue()
    {
        return GetPendingAutoTrackReconcileShowIds().Count > 0;
    }

    private HashSet<long> GetPendingAutoTrackReconcileShowIds()
    {
        var pendingShowIds = new HashSet<long>();

        foreach (var order in _databaseService.GetTorrentCartOrders())
        {
            if (order.Source != TorrentOrderSource.AutoTrack ||
                order.TargetKind != MediaKind.TvEpisode)
            {
                continue;
            }

            if (order.Status is TorrentOrderStatus.AddedToClient or TorrentOrderStatus.Downloading)
            {
                pendingShowIds.Add(order.MediaId);
            }
        }

        foreach (var show in _trackedShowService.GetAutoTrackedShows())
        {
            if (!show.AutoTrackAutoReconcileAndLink)
            {
                continue;
            }

            foreach (var episode in _trackedShowService.GetEpisodes(show.Id))
            {
                if (!string.IsNullOrWhiteSpace(episode.TorrentHash) &&
                    episode.Availability != EpisodeAvailability.Available)
                {
                    pendingShowIds.Add(show.Id);
                    break;
                }
            }
        }

        return pendingShowIds;
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

    private List<(TrackedShow Show, IReadOnlyList<TrackedEpisode> Episodes)> BuildHuntQueue(
        IReadOnlyList<TrackedShow> shows,
        bool bypassSchedule,
        bool resumeOnly)
    {
        var settings = GetAutoTrackSettings();
        var huntDelayHours = Math.Clamp(settings.HuntMinHoursAfterAirDate, 0, 48);
        var maxEpisodesPerShow = Math.Clamp(settings.Search.MaxEpisodesPerShowPerHuntCycle, 1, 50);
        var nowLocal = DateTime.Now;
        var queue = new List<(TrackedShow Show, IReadOnlyList<TrackedEpisode> Episodes)>();
        foreach (var show in shows.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(show.AutoTrackDownloadFolder))
            {
                continue;
            }

            if (!bypassSchedule &&
                !resumeOnly &&
                !AutoTrackWeekAnchor.IsPastAnchorThisWeek(show, nowLocal, settings))
            {
                continue;
            }

            var episodes = _trackedShowService.GetEpisodes(show.Id);
            var pending = AutoTrackTmdbEligibility.FindPendingEpisodes(
                show,
                episodes,
                IsAutoTrackHuntBlocked,
                nowLocal,
                resumeOnly ? 0 : huntDelayHours);
            if (pending.Count == 0)
            {
                continue;
            }

            List<TrackedEpisode> batch;
            if (resumeOnly)
            {
                batch = pending
                    .Where(episode =>
                        _torrentCartService.TryGetAutoTrackHuntBlockingEpisodeOrder(episode.Id, out var blocking) &&
                        blocking is not null &&
                        blocking.Status == TorrentOrderStatus.CandidatesFound)
                    .Take(maxEpisodesPerShow)
                    .ToList();
                if (batch.Count == 0)
                {
                    continue;
                }
            }
            else
            {
                batch = pending.Take(maxEpisodesPerShow).ToList();
            }

            queue.Add((show, batch));
        }

        return queue;
    }

    private static string FormatHuntBatchStage(IReadOnlyList<TrackedEpisode> episodes)
    {
        var line = AutoTrackTmdbEligibility.FormatHuntStatusLine(episodes);
        return string.IsNullOrEmpty(line) ? "No pending hunt" : line.Replace("Hunting: ", "Hunting ", StringComparison.Ordinal);
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

        var warpOwnedByUs = false;
        var warpEnabled = _settingsService.Current.Warp?.Enabled ?? true;
        if (warpEnabled && _warpCliService.IsAvailable)
        {
            var timeout = TimeSpan.FromSeconds(_settingsService.Current.Warp?.ConnectTimeoutSeconds ?? 30);
            var attempt = await _warpCliService.ConnectOwnedAsync(timeout, cancellationToken);
            warpOwnedByUs = attempt.Owned;
            if (!attempt.Connected)
            {
                _logger.Warning("Auto-track continuing without WARP (connect failed or timed out).", LogTarget.All);
            }
            else if (attempt.Owned)
            {
                var orderCount = pendingOrdersByShow.Sum(entry => entry.Value.Orders.Count);
                _logger.Info(
                    $"WARP connected for hunt fetch/add ({pendingOrdersByShow.Count} show(s), {orderCount} order(s)).",
                    LogTarget.File | LogTarget.Console);
            }
            else
            {
                _logger.Info(
                    "WARP already connected; Auto-Track hunt will reuse the existing session (not owned).",
                    LogTarget.File | LogTarget.Console);
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
                                show,
                                NotificationKind.AutoTrackHuntProgress);
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
                        show,
                        NotificationKind.AutoTrackHuntProgress);
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
                        var candidates = _torrentCartService.GetCandidates(order.Id)
                            .OrderBy(candidate => candidate.Rank)
                            .ToList();
                        if (candidates.Count == 0)
                        {
                            result.Failed++;
                            _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Failed, "No approved candidates available.");
                            continue;
                        }

                        var failedCandidateUrls = GetFailedCandidateUrls(order);
                        var candidatesToTry = candidates
                            .Where(candidate => !failedCandidateUrls.Contains(candidate.Url))
                            .ToList();
                        if (candidatesToTry.Count == 0)
                        {
                            result.Failed++;
                            _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Failed, "All candidates have already failed.");
                            continue;
                        }

                        var added = false;
                        for (var index = 0; index < candidatesToTry.Count; index++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var candidate = candidatesToTry[index];
                            if (!string.Equals(order.SelectedCandidateUrl, candidate.Url, StringComparison.Ordinal))
                            {
                                SelectCandidateForRetry(order, candidate);
                            }

                            try
                            {
                                await AddOrderToClientAsync(order, savePath, cancellationToken);
                                result.TorrentsAdded++;
                                NotifyStage(
                                    "Auto-Track",
                                    $"{show.DisplayTitle} — Added {order.Title} → {savePath}",
                                    show,
                                    NotificationKind.AutoTrackHuntProgress);
                                added = true;
                                break;
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                failedCandidateUrls.Add(candidate.Url);
                                order.FailedCandidateUrls = SerializeFailedCandidateUrls(failedCandidateUrls);
                                order.LastFailureReason = ex.Message;
                                order.StatusDetail = $"Candidate failed: {candidate.Name} — {ex.Message}";
                                _torrentCartService.SaveOrder(order);
                                _logger.Warning(
                                    $"Auto-track add failed for '{order.Title}' candidate '{candidate.Name}': {ex.Message}",
                                    LogTarget.All);
                            }
                        }

                        if (!added)
                        {
                            result.Failed++;
                            order.Status = TorrentOrderStatus.Failed;
                            order.StatusDetail = string.IsNullOrWhiteSpace(order.LastFailureReason)
                                ? "All candidates failed."
                                : $"All candidates failed. Last error: {order.LastFailureReason}";
                            _torrentCartService.SaveOrder(order);
                        }
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
        finally
        {
            if (warpOwnedByUs)
            {
                var orderCount = pendingOrdersByShow.Sum(entry => entry.Value.Orders.Count);
                _logger.Info(
                    $"Disconnecting WARP after hunt fetch/add ({pendingOrdersByShow.Count} show(s), {orderCount} order(s)).",
                    LogTarget.File | LogTarget.Console);
                await _warpCliService.DisconnectAsync(CancellationToken.None);
            }
        }
    }

    private void SelectCandidateForRetry(TorrentCartOrder order, TorrentCartOrderCandidate candidate)
    {
        _databaseService.UpdateTorrentCartOrderCandidateSelection(order.Id, candidate.Id);
        order.SelectedCandidateName = candidate.Name;
        order.SelectedCandidateUrl = candidate.Url;
        order.SelectedCandidatePlugin = candidate.PluginName;
        order.SelectedCandidateFileSize = candidate.FileSize;
        order.SelectedCandidateSeeders = candidate.Seeders;
        order.SelectedCandidateLeechers = candidate.Leechers;
        order.SelectedCandidateQuality = candidate.Quality;
        order.SelectedCandidateAudioCodec = candidate.AudioCodec;
        order.SelectedCandidateCoveredSeasons = candidate.CoveredSeasons;
        order.SelectedCandidateContentProfile = candidate.ContentProfileJson;
        order.SelectedCandidateTotalScore = candidate.TotalScore;
        order.StatusDetail = $"Retrying with candidate: {candidate.Name}";
        _torrentCartService.SaveOrder(order);
    }

    private static HashSet<string> GetFailedCandidateUrls(TorrentCartOrder order)
    {
        if (string.IsNullOrWhiteSpace(order.FailedCandidateUrls))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var urls = JsonSerializer.Deserialize<List<string>>(order.FailedCandidateUrls) ?? [];
            return new HashSet<string>(urls.Where(url => !string.IsNullOrWhiteSpace(url)), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string SerializeFailedCandidateUrls(IEnumerable<string> urls)
    {
        return JsonSerializer.Serialize(urls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct(StringComparer.OrdinalIgnoreCase));
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
        var autoTrack = _settingsService.Current.AutoTrack;
        autoTrack.Search ??= new AutoTrackSearchSettings();
        autoTrack.Quality ??= new AutoTrackQualityPolicy();
        return autoTrack;
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

    private void NotifyStage(string title, string message, TrackedShow show, NotificationKind kind)
    {
        var poster = _posterImageService.GetNotificationHeroImage(
            MediaKind.TvEpisode,
            show.TmdbId,
            show.PosterPath);
        _windowsNotificationService.TryShow(new WindowsNotificationRequest
        {
            Title = title,
            Message = message,
            Kind = kind,
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
            show,
            NotificationKind.AutoTrackHardlinked);
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
            Kind = NotificationKind.AutoTrackRunSummary,
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
