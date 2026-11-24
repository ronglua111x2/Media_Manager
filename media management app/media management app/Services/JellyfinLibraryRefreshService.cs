using System.Net.Http;
using System.Net.Http.Headers;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class JellyfinLibraryRefreshService : IJellyfinLibraryRefreshService
{
    private static readonly TimeSpan DebounceDelay = JellyfinRetryBackoff.Initial;
    private static readonly TimeSpan TaskPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LogPollInterval = TimeSpan.FromSeconds(2);

    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _databaseService;
    private readonly IJellyfinClient _jellyfinClient;
    private readonly IWarpCliService _warpCliService;
    private readonly IWindowsNotificationService _windowsNotificationService;
    private readonly HttpClient _httpClient;
    private readonly IAppLogger _logger;
    private readonly object _queueLock = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private CancellationTokenSource? _debounceCts;
    // Guarded by _queueLock: failed flushes in a row; drives JellyfinRetryBackoff.
    private int _consecutiveFlushFailures;
    // Set by the TMDB pre-probe when WARP could not connect; read after a failed flush.
    private bool _lastWarpConnectFailed;
    private bool _disposed;

    public JellyfinLibraryRefreshService(
        ISettingsService settingsService,
        IDatabaseService databaseService,
        IJellyfinClient jellyfinClient,
        IWarpCliService warpCliService,
        IWindowsNotificationService windowsNotificationService,
        HttpClient httpClient,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;
        _jellyfinClient = jellyfinClient;
        _warpCliService = warpCliService;
        _windowsNotificationService = windowsNotificationService;
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

            if (added == 0)
            {
                // Already queued; keep the current (possibly backed-off) retry timer.
                return;
            }

            _consecutiveFlushFailures = 0;
            ScheduleDebounceLocked(DebounceDelay);
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

        await FlushBatchAsync(paths, notified: null, cancellationToken);
    }

    public bool IsConfigured => IsFeatureConfigured(out _);

    public async Task<JellyfinRefreshNowResult> RefreshNowAsync(
        IEnumerable<string> symlinkPaths,
        CancellationToken cancellationToken = default)
    {
        var requested = (symlinkPaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existing = requested
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToList();
        var skipped = requested.Count - existing.Count;

        if (!IsFeatureConfigured(out _))
        {
            _logger.Info(
                $"Jellyfin refresh now: not configured; {requested.Count} path(s) not sent.",
                LogTarget.File | LogTarget.Console);
            return new JellyfinRefreshNowResult(JellyfinRefreshNowOutcome.NotConfigured, 0, skipped);
        }

        if (existing.Count == 0)
        {
            _logger.Info(
                $"Jellyfin refresh now: nothing to send (requested={requested.Count}, missing on disk={skipped}).",
                LogTarget.All);
            return new JellyfinRefreshNowResult(JellyfinRefreshNowOutcome.NothingToSend, 0, skipped);
        }

        List<string> batch;
        int mergedPending;
        lock (_queueLock)
        {
            // Merge with the pending debounce batch and send it now, so nothing goes out twice.
            CancelDebounceLocked();
            mergedPending = _pendingPaths.Count(path => !existing.Contains(path, StringComparer.OrdinalIgnoreCase));
            foreach (var path in existing)
            {
                _pendingPaths.Add(path);
            }

            batch = _pendingPaths.ToList();
            _pendingPaths.Clear();
            _consecutiveFlushFailures = 0;
        }

        _logger.Info(
            $"Jellyfin refresh now: sending {existing.Count} path(s) (+{mergedPending} pending merged, {skipped} missing skipped).",
            LogTarget.All);

        // The flush may hold WARP for minutes after the notify; run it in the background and
        // return as soon as Jellyfin accepted or rejected the notify.
        var notified = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var flushTask = Task.Run(() => FlushBatchAsync(batch, notified, CancellationToken.None));
        _ = flushTask.ContinueWith(
            task => _logger.Error("Jellyfin refresh now: background flush failed.", task.Exception, LogTarget.All),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        await Task.WhenAny(notified.Task, flushTask).WaitAsync(cancellationToken);
        var error = notified.Task.IsCompleted
            ? notified.Task.Result
            : flushTask.Exception?.GetBaseException()
              ?? new InvalidOperationException("flush ended before the notify");

        _logger.Info(
            error is null
                ? $"Jellyfin refresh now: notify accepted for {existing.Count} path(s)."
                : $"Jellyfin refresh now: notify failed for {existing.Count} path(s): {error.Message}",
            LogTarget.All);

        return error is null
            ? new JellyfinRefreshNowResult(JellyfinRefreshNowOutcome.Sent, existing.Count, skipped)
            : new JellyfinRefreshNowResult(JellyfinRefreshNowOutcome.Failed, existing.Count, skipped, error.Message);
    }

    private async Task FlushBatchAsync(
        List<string> paths,
        TaskCompletionSource<Exception?>? notified,
        CancellationToken cancellationToken)
    {
        bool requeued;
        try
        {
            requeued = await FlushPathsAsync(paths, notified, cancellationToken);
        }
        finally
        {
            notified?.TrySetResult(new InvalidOperationException("flush ended before the notify"));
        }
        if (requeued)
        {
            TimeSpan retryDelay;
            lock (_queueLock)
            {
                foreach (var path in paths)
                {
                    _pendingPaths.Add(path);
                }

                _consecutiveFlushFailures++;
                retryDelay = JellyfinRetryBackoff.Next(_consecutiveFlushFailures, _lastWarpConnectFailed);
                ScheduleDebounceLocked(retryDelay);
            }

            _logger.Warning(
                $"Jellyfin refresh: kept {paths.Count} path(s) queued for retry (server unreachable or notify failed); next retry in {FormatDelay(retryDelay)} (failures in a row={_consecutiveFlushFailures}).",
                LogTarget.All);
        }
        else
        {
            lock (_queueLock)
            {
                _consecutiveFlushFailures = 0;
            }
        }
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

    private void ScheduleDebounceLocked(TimeSpan delay)
    {
        CancelDebounceLocked();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
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

    /// <returns>True when paths should be put back on the pending queue.</returns>
    private async Task<bool> FlushPathsAsync(
        IReadOnlyList<string> paths,
        TaskCompletionSource<Exception?>? notified,
        CancellationToken cancellationToken)
    {
        if (!IsFeatureConfigured(out var jellyfinSettings))
        {
            notified?.TrySetResult(new InvalidOperationException("Jellyfin refresh is not configured"));
            _logger.Info(
                $"Jellyfin refresh skipped {paths.Count} path(s): feature disabled or not configured.",
                LogTarget.All);
            return false;
        }

        await _flushGate.WaitAsync(cancellationToken);
        var heldOwnedWarp = false;
        var notifySucceeded = false;
        var refreshWindowMessage = "Hold expired.";
        try
        {
            _logger.Info(
                $"Jellyfin refresh flush starting for {paths.Count} path(s) → {jellyfinSettings.BaseUrl}.",
                LogTarget.All);

            var recoveredWithWarp = await EnsureTmdbReachableWithWarpAsync(cancellationToken);

            await _jellyfinClient.ReportMediaUpdatedAsync(paths, cancellationToken);
            notifySucceeded = true;
            notified?.TrySetResult(null);

            _windowsNotificationService.TryShow(new WindowsNotificationRequest
            {
                Title = "Jellyfin",
                Message = $"Path notify sent ({paths.Count} path(s)).",
                Kind = NotificationKind.JellyfinPathNotified
            });

            // Hold WARP while Jellyfin fetches TMDB metadata: when the pre-probe needed WARP, or a hunt holds it.
            if (recoveredWithWarp || _warpCliService.HasAutoTrackPipelineLease)
            {
                // The pre-probe already holds the JellyfinHold lease with WARP up.
                var holdConnected = recoveredWithWarp;
                if (!holdConnected)
                {
                    var timeout = TimeSpan.FromSeconds(_settingsService.Current.Warp?.ConnectTimeoutSeconds ?? 30);
                    var holdLease = await _warpCliService.AcquireAsync(
                        WarpLeaseReason.JellyfinHold,
                        timeout,
                        cancellationToken);
                    holdConnected = holdLease.Connected;
                }

                if (holdConnected)
                {
                    heldOwnedWarp = true;
                    refreshWindowMessage = await HoldWarpForJellyfinAsync(jellyfinSettings, cancellationToken);
                }
                else
                {
                    _logger.Warning(
                        "Jellyfin refresh: WARP hold lease could not connect; skipping post-notify hold.",
                        LogTarget.All);
                }
            }
            else
            {
                _logger.Info(
                    "Jellyfin refresh: TMDB reachable without WARP and no Auto-Track WARP lease; skipping post-notify hold.",
                    LogTarget.All);
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!notifySucceeded &&
                                   JellyfinNotifyPolicy.DropPaths(NetworkFailureClassifier.Classify(ex, cancellationToken)))
        {
            _logger.Error(
                $"Jellyfin rejected the path notify; dropping {paths.Count} path(s). Check the Jellyfin API key.",
                ex,
                LogTarget.All);
            notified?.TrySetResult(new InvalidOperationException($"Jellyfin rejected the notify (check the API key): {ex.Message}", ex));
            return false;
        }
        catch (Exception ex) when (!notifySucceeded && IsTransientJellyfinFailure(ex))
        {
            _logger.Warning(
                $"Jellyfin refresh flush deferred for {paths.Count} path(s): {ex.Message}",
                LogTarget.All);
            notified?.TrySetResult(new InvalidOperationException($"Jellyfin unreachable, kept queued for retry: {ex.Message}", ex));
            return true;
        }
        catch (Exception ex) when (!notifySucceeded)
        {
            _logger.Error($"Jellyfin refresh flush failed for {paths.Count} path(s); re-queueing.", ex, LogTarget.All);
            notified?.TrySetResult(ex);
            return true;
        }
        catch (Exception ex)
        {
            // Notify already succeeded; do not re-queue. Log hold/post-notify failures only.
            _logger.Warning(
                $"Jellyfin refresh post-notify step failed after paths were accepted: {ex.Message}",
                LogTarget.All);
            return false;
        }
        finally
        {
            try
            {
                await _warpCliService.ReleaseAsync(WarpLeaseReason.JellyfinHold, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Warning($"WARP Jellyfin hold lease release failed: {ex.Message}", LogTarget.All);
            }

            if (heldOwnedWarp)
            {
                _windowsNotificationService.TryShow(new WindowsNotificationRequest
                {
                    Title = "Jellyfin",
                    Message = refreshWindowMessage,
                    Kind = NotificationKind.JellyfinRefreshWindowEnded
                });
            }

            _flushGate.Release();
        }
    }

    private static string FormatDelay(TimeSpan delay) =>
        delay.TotalMinutes >= 1 ? $"{delay.TotalMinutes:0} min" : $"{delay.TotalSeconds:0}s";

    private static bool IsTransientJellyfinFailure(Exception ex)
    {
        return NetworkFailureClassifier.Classify(ex) != NetFailure.Unknown;
    }

    private async Task<string> HoldWarpForJellyfinAsync(
        JellyfinRefreshSettings jellyfinSettings,
        CancellationToken cancellationToken)
    {
        var holdSeconds = Math.Clamp(
            jellyfinSettings.WarpHoldSecondsAfterNotify <= 0
                ? JellyfinRefreshSettings.DefaultWarpHoldSecondsAfterNotify
                : jellyfinSettings.WarpHoldSecondsAfterNotify,
            JellyfinRefreshSettings.MinWarpHoldSecondsAfterNotify,
            JellyfinRefreshSettings.MaxWarpHoldSecondsAfterNotify);
        var maxHoldSeconds = Math.Min(
            JellyfinRefreshSettings.MaxWarpHoldSecondsAfterNotify,
            holdSeconds * 2);
        var holdDeadline = DateTime.UtcNow.AddSeconds(holdSeconds);
        var hardDeadline = DateTime.UtcNow.AddSeconds(maxHoldSeconds);

        JellyfinLogTailer? tailer = null;
        if (jellyfinSettings.EnableLogEarlyDisconnect)
        {
            if (!JellyfinLogPathValidator.TryResolveAndValidate(
                    jellyfinSettings.LogPath,
                    out var resolvedPath,
                    out var validateError))
            {
                _logger.Info(
                    $"Jellyfin log early disconnect skipped: invalid log path — {validateError}",
                    LogTarget.All);
            }
            else if (!JellyfinLogTailer.TryOpen(resolvedPath, out tailer, out var openError))
            {
                _logger.Info(
                    $"Jellyfin log early disconnect skipped: {openError}",
                    LogTarget.All);
            }
            else
            {
                _logger.Info(
                    $"Jellyfin log early disconnect enabled: tailing '{tailer!.FilePath}' (quiet={ClampQuietSeconds(jellyfinSettings)}s).",
                    LogTarget.All);
            }
        }

        try
        {
            if (tailer is not null)
            {
                return await HoldWarpWithLogAsync(
                    jellyfinSettings,
                    tailer,
                    holdSeconds,
                    maxHoldSeconds,
                    holdDeadline,
                    hardDeadline,
                    cancellationToken);
            }

            return await HoldWarpTimerOnlyAsync(
                holdSeconds,
                maxHoldSeconds,
                holdDeadline,
                hardDeadline,
                cancellationToken);
        }
        finally
        {
            tailer?.Dispose();
        }
    }

    private async Task<string> HoldWarpWithLogAsync(
        JellyfinRefreshSettings jellyfinSettings,
        JellyfinLogTailer tailer,
        int holdSeconds,
        int maxHoldSeconds,
        DateTime holdDeadline,
        DateTime hardDeadline,
        CancellationToken cancellationToken)
    {
        var quietSeconds = ClampQuietSeconds(jellyfinSettings);
        var sawRefreshStart = false;
        DateTime? lastActivityUtc = null;
        var sawLibraryTaskRunning = false;
        var endedBecauseTaskIdle = false;
        var endedBecauseLogQuiet = false;

        _logger.Info(
            $"Jellyfin refresh WARP hold starting (log mode): hold={holdSeconds}s, quiet={quietSeconds}s, max={maxHoldSeconds}s.",
            LogTarget.All);

        while (DateTime.UtcNow < hardDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var line in tailer.ReadNewLines())
            {
                if (!JellyfinLogTailer.IsActivityLine(line))
                {
                    continue;
                }

                lastActivityUtc = DateTime.UtcNow;
                if (JellyfinLogTailer.IsRefreshStartLine(line))
                {
                    if (!sawRefreshStart)
                    {
                        sawRefreshStart = true;
                        _logger.Info(
                            $"Jellyfin log: first refresh start seen — {TruncateForLog(line)}",
                            LogTarget.All);
                    }
                    else
                    {
                        _logger.Info(
                            $"Jellyfin log: refresh activity — {TruncateForLog(line)}",
                            LogTarget.All);
                    }
                }
                else
                {
                    _logger.Debug(
                        $"Jellyfin log: metadata activity — {TruncateForLog(line)}",
                        LogTarget.File | LogTarget.Console);
                }
            }

            if (sawRefreshStart &&
                lastActivityUtc.HasValue &&
                DateTime.UtcNow - lastActivityUtc.Value >= TimeSpan.FromSeconds(quietSeconds))
            {
                endedBecauseLogQuiet = true;
                _logger.Info(
                    $"Jellyfin log quiet for {quietSeconds}s after refresh; early WARP disconnect.",
                    LogTarget.All);
                break;
            }

            var runningLibraryTasks = await TryGetRunningLibraryTasksAsync(cancellationToken);
            if (runningLibraryTasks.Count > 0)
            {
                sawLibraryTaskRunning = true;
                var names = string.Join(", ", runningLibraryTasks.Select(task => task.Name ?? task.Key ?? "task"));
                _logger.Info(
                    $"Jellyfin library task(s) still running during WARP hold: {names}. Extending until idle (max {maxHoldSeconds}s).",
                    LogTarget.All);
            }
            else if (sawLibraryTaskRunning)
            {
                endedBecauseTaskIdle = true;
                _logger.Info(
                    "Jellyfin library task(s) returned to Idle during WARP hold.",
                    LogTarget.All);
                break;
            }
            else if (!sawRefreshStart && DateTime.UtcNow >= holdDeadline)
            {
                _logger.Info(
                    $"Jellyfin refresh WARP hold of {holdSeconds}s elapsed with no log refresh start.",
                    LogTarget.All);
                break;
            }

            var remainingToHard = hardDeadline - DateTime.UtcNow;
            if (remainingToHard <= TimeSpan.Zero)
            {
                break;
            }

            var delay = TimeSpan.FromMilliseconds(
                Math.Min(LogPollInterval.TotalMilliseconds, remainingToHard.TotalMilliseconds));
            await Task.Delay(delay, cancellationToken);
        }

        if (endedBecauseLogQuiet)
        {
            return "Refresh window ended (log quiet after refresh).";
        }

        if (DateTime.UtcNow >= hardDeadline && !endedBecauseTaskIdle)
        {
            _logger.Warning(
                $"Jellyfin refresh WARP hold hit max {maxHoldSeconds}s; disconnecting.",
                LogTarget.All);
            return sawLibraryTaskRunning
                ? $"Refresh window ended (max hold {maxHoldSeconds}s; library task still running)."
                : $"Refresh window ended (max hold {maxHoldSeconds}s).";
        }

        return endedBecauseTaskIdle
            ? "Refresh window ended (library task idle)."
            : $"Refresh window ended (hold {holdSeconds}s expired).";
    }

    private async Task<string> HoldWarpTimerOnlyAsync(
        int holdSeconds,
        int maxHoldSeconds,
        DateTime holdDeadline,
        DateTime hardDeadline,
        CancellationToken cancellationToken)
    {
        var sawLibraryTaskRunning = false;
        var endedBecauseTaskIdle = false;

        _logger.Info(
            $"Jellyfin refresh WARP hold starting: hold={holdSeconds}s, max={maxHoldSeconds}s.",
            LogTarget.All);

        while (DateTime.UtcNow < hardDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var runningLibraryTasks = await TryGetRunningLibraryTasksAsync(cancellationToken);
            if (runningLibraryTasks.Count > 0)
            {
                sawLibraryTaskRunning = true;
                var names = string.Join(", ", runningLibraryTasks.Select(task => task.Name ?? task.Key ?? "task"));
                _logger.Info(
                    $"Jellyfin library task(s) still running during WARP hold: {names}. Extending until idle (max {maxHoldSeconds}s).",
                    LogTarget.All);
            }
            else if (sawLibraryTaskRunning)
            {
                endedBecauseTaskIdle = true;
                _logger.Info(
                    "Jellyfin library task(s) returned to Idle during WARP hold.",
                    LogTarget.All);
                break;
            }
            else if (DateTime.UtcNow >= holdDeadline)
            {
                _logger.Info(
                    $"Jellyfin refresh WARP hold of {holdSeconds}s elapsed with no running library task.",
                    LogTarget.All);
                break;
            }

            var remainingToHard = hardDeadline - DateTime.UtcNow;
            if (remainingToHard <= TimeSpan.Zero)
            {
                break;
            }

            var delay = TimeSpan.FromMilliseconds(
                Math.Min(TaskPollInterval.TotalMilliseconds, remainingToHard.TotalMilliseconds));
            await Task.Delay(delay, cancellationToken);
        }

        if (DateTime.UtcNow >= hardDeadline && !endedBecauseTaskIdle)
        {
            _logger.Warning(
                $"Jellyfin refresh WARP hold hit max {maxHoldSeconds}s; disconnecting.",
                LogTarget.All);
            return sawLibraryTaskRunning
                ? $"Refresh window ended (max hold {maxHoldSeconds}s; library task still running)."
                : $"Refresh window ended (max hold {maxHoldSeconds}s).";
        }

        return endedBecauseTaskIdle
            ? "Refresh window ended (library task idle)."
            : $"Refresh window ended (hold {holdSeconds}s expired).";
    }

    private static int ClampQuietSeconds(JellyfinRefreshSettings jellyfinSettings) =>
        Math.Clamp(
            jellyfinSettings.LogQuietSecondsAfterRefresh <= 0
                ? JellyfinRefreshSettings.DefaultLogQuietSecondsAfterRefresh
                : jellyfinSettings.LogQuietSecondsAfterRefresh,
            JellyfinRefreshSettings.MinLogQuietSecondsAfterRefresh,
            JellyfinRefreshSettings.MaxLogQuietSecondsAfterRefresh);

    private static string TruncateForLog(string line, int maxLength = 160)
    {
        var trimmed = line.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }

    private async Task<IReadOnlyList<JellyfinScheduledTaskInfo>> TryGetRunningLibraryTasksAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var tasks = await _jellyfinClient.GetScheduledTasksAsync(cancellationToken);
            return tasks
                .Where(task => task.IsLibraryRefreshRelated && task.IsRunning)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Debug(
                $"Jellyfin ScheduledTasks poll failed during WARP hold: {ex.Message}",
                LogTarget.File | LogTarget.Console);
            return [];
        }
    }

    /// <returns>True when TMDB was reached only after connecting WARP (lease <see cref="WarpLeaseReason.JellyfinHold"/> held).</returns>
    private async Task<bool> EnsureTmdbReachableWithWarpAsync(CancellationToken cancellationToken)
    {
        _lastWarpConnectFailed = false;
        if (string.IsNullOrWhiteSpace(_settingsService.Current.TmdbReadAccessToken))
        {
            _logger.Warning(
                "TMDB read access token missing; skipping pre-probe before Jellyfin refresh.",
                LogTarget.All);
            return false;
        }

        try
        {
            await ProbeTmdbAsync(cancellationToken);
            _logger.Info("TMDB pre-probe OK before Jellyfin refresh.", LogTarget.All);
            return false;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var failure = NetworkFailureClassifier.Classify(ex, cancellationToken);
            if (JellyfinNotifyPolicy.NotifyWithoutTunnel(failure, CanAutoRecoverOnNetworkBlock()))
            {
                _logger.Warning(
                    $"TMDB pre-probe failed ({failure}): {ex.Message}. Notifying Jellyfin anyway; its metadata fetch may fail.",
                    LogTarget.All);
                return false;
            }

            var timeout = TimeSpan.FromSeconds(_settingsService.Current.Warp?.ConnectTimeoutSeconds ?? 30);
            try
            {
                await TmdbWarpRecovery.RetryWithWarpAsync(
                    "Jellyfin refresh pre-probe",
                    ex,
                    async ct =>
                    {
                        await ProbeTmdbAsync(ct);
                        return true;
                    },
                    ct => _warpCliService.AcquireAsync(WarpLeaseReason.JellyfinHold, timeout, ct),
                    _logger,
                    cancellationToken);
            }
            catch (TmdbWarpConnectFailedException)
            {
                _lastWarpConnectFailed = true;
                throw;
            }
            catch (Exception retryEx) when (!cancellationToken.IsCancellationRequested &&
                                            !TmdbNetworkBlockDetector.IsLikelyBlocked(retryEx, cancellationToken))
            {
                // WARP is up and TMDB answered with something a tunnel cannot fix (e.g. 401): still notify and hold.
                _logger.Warning(
                    $"TMDB pre-probe through WARP failed ({NetworkFailureClassifier.Classify(retryEx, cancellationToken)}): {retryEx.Message}. Notifying Jellyfin anyway.",
                    LogTarget.All);
                return true;
            }

            _logger.Info("TMDB pre-probe OK after WARP.", LogTarget.All);
            return true;
        }
    }

    private async Task ProbeTmdbAsync(CancellationToken cancellationToken)
    {
        var token = _settingsService.Current.TmdbReadAccessToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("TMDB read access token is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{NetworkEndpoints.TmdbLegacyApiHost}/3/configuration");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"TMDB probe failed: {(int)response.StatusCode} {response.ReasonPhrase}",
                null,
                response.StatusCode);
        }
    }

    private bool CanAutoRecoverOnNetworkBlock()
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
