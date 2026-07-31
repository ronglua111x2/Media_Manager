using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class AutoTrackSchedulerService : IAutoTrackSchedulerService
{
    private static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ReconcileArmPollInterval = TimeSpan.FromSeconds(2);

    private readonly ISettingsService _settingsService;
    private readonly IAutoTrackService _autoTrackService;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _startLock = new();
    private readonly object _disposeLock = new();
    private readonly object _reconcileArmLock = new();
    private Task? _worker;
    private bool _disposed;
    private bool _reconcileArmed;

    public AutoTrackSchedulerService(
        ISettingsService settingsService,
        IAutoTrackService autoTrackService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _autoTrackService = autoTrackService;
        _logger = logger;
    }

    public event EventHandler<AutoTrackRunResult>? RunCompleted;

    public void Start()
    {
        lock (_startLock)
        {
            _worker ??= Task.Run(RunAsync);
        }
    }

    public void RequestReconcileAfterAdds()
    {
        lock (_reconcileArmLock)
        {
            _reconcileArmed = true;
        }

        _logger.Info("Auto-track reconcile armed after successful torrent add(s).", LogTarget.File | LogTarget.Console);
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

        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            if (_worker is not null && !_worker.Wait(ShutdownWaitTimeout))
            {
                _logger.Warning(
                    $"Auto-track scheduler did not stop within {ShutdownWaitTimeout.TotalSeconds:0} seconds.",
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
    }

    private async Task RunAsync()
    {
        // Hunt is driven by TMDB discovery (new/pending episodes), not a blind resume timer.
        await Task.WhenAll(
            RunTmdbDiscoveryLoopAsync(),
            RunReconcileLoopAsync());
    }

    private async Task RunTmdbDiscoveryLoopAsync()
    {
        if (IsDisposed())
        {
            return;
        }

        await RunTmdbDiscoverySafelyAsync();

        while (!IsDisposed() && !_shutdown.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(GetTmdbInterval(), _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunTmdbDiscoverySafelyAsync();
        }
    }

    private async Task RunReconcileLoopAsync()
    {
        if (IsDisposed())
        {
            return;
        }

        // One-shot on start only if a pending download/link queue already exists.
        if (_autoTrackService.HasPendingAutoTrackDownloadQueue())
        {
            RequestReconcileAfterAdds();
        }

        while (!IsDisposed() && !_shutdown.Token.IsCancellationRequested)
        {
            if (!IsReconcileArmed())
            {
                try
                {
                    await Task.Delay(ReconcileArmPollInterval, _shutdown.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            if (!_autoTrackService.HasPendingAutoTrackDownloadQueue())
            {
                DisarmReconcile("no pending download queue");
                continue;
            }

            await RunReconcileSafelyAsync();

            if (!_autoTrackService.HasPendingAutoTrackDownloadQueue())
            {
                DisarmReconcile("queue cleared after reconcile pass");
                continue;
            }

            try
            {
                await Task.Delay(GetReconcileInterval(), _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private bool IsReconcileArmed()
    {
        lock (_reconcileArmLock)
        {
            return _reconcileArmed;
        }
    }

    private void DisarmReconcile(string reason)
    {
        lock (_reconcileArmLock)
        {
            if (!_reconcileArmed)
            {
                return;
            }

            _reconcileArmed = false;
        }

        _logger.Info($"Auto-track reconcile disarmed: {reason}.", LogTarget.File | LogTarget.Console);
    }

    private TimeSpan GetTmdbInterval()
    {
        var minutes = Math.Clamp(_settingsService.Current.AutoTrack?.TmdbCheckIntervalMinutes ?? 30, 5, 1440);
        return TimeSpan.FromMinutes(minutes);
    }

    private TimeSpan GetReconcileInterval()
    {
        var minutes = Math.Clamp(_settingsService.Current.AutoTrack?.ReconcileIntervalMinutes ?? 10, 5, 1440);
        return TimeSpan.FromMinutes(minutes);
    }

    private async Task RunTmdbDiscoverySafelyAsync()
    {
        if (!(_settingsService.Current.AutoTrack?.Enabled ?? true))
        {
            return;
        }

        try
        {
            var result = await _autoTrackService.RunTmdbDiscoveryAsync(cancellationToken: _shutdown.Token);
            if (IsMeaningfulAutoTrackResult(result))
            {
                _logger.Info($"Auto-track TMDB discovery cycle complete. {result.Summary}", LogTarget.All);
                _autoTrackService.RecordRunResult(result);
                NotifyRunCompleted(result);
            }
            else
            {
                _logger.Debug(
                    $"Auto-track TMDB discovery idle. {result.Summary}",
                    LogTarget.File | LogTarget.Console);
            }

            ArmReconcileIfTorrentsAdded(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var result = FailedResult($"TMDB discovery failed: {ex.Message}");
            _logger.Warning($"Auto-track TMDB discovery loop failed: {ex.Message}", LogTarget.All);
            NotifyRunCompleted(result);
        }
    }

    private async Task RunReconcileSafelyAsync()
    {
        if (!(_settingsService.Current.AutoTrack?.Enabled ?? true))
        {
            return;
        }

        _logger.Info("Auto-track reconcile cycle starting.", LogTarget.All);

        try
        {
            var result = await _autoTrackService.RunBackgroundReconcileAsync(_shutdown.Token);
            _logger.Info($"Auto-track reconcile cycle complete. {result.Summary}", LogTarget.All);

            // Mirror TMDB discovery: refresh News/Auto dashboards (and tray) when reconcile changed state.
            if (IsMeaningfulAutoTrackResult(result))
            {
                _autoTrackService.RecordRunResult(result);
                NotifyRunCompleted(result);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var result = FailedResult($"Reconcile failed: {ex.Message}");
            _logger.Warning($"Auto-track reconcile loop failed: {ex.Message}", LogTarget.All);
            NotifyRunCompleted(result);
        }
    }

    private void ArmReconcileIfTorrentsAdded(AutoTrackRunResult result)
    {
        if (result.TorrentsAdded > 0)
        {
            RequestReconcileAfterAdds();
        }
    }

    private void NotifyRunCompleted(AutoTrackRunResult result)
    {
        RunCompleted?.Invoke(this, result);
    }

    private static bool IsMeaningfulAutoTrackResult(AutoTrackRunResult result)
    {
        return result.TmdbRefreshed > 0 ||
               result.EpisodesQueued > 0 ||
               result.CandidatesFound > 0 ||
               result.TorrentsAdded > 0 ||
               result.LinkedCount > 0 ||
               result.Failed > 0 ||
               !result.Succeeded;
    }

    private static AutoTrackRunResult FailedResult(string summary)
    {
        return new AutoTrackRunResult
        {
            Succeeded = false,
            Summary = summary
        };
    }

    private bool IsDisposed()
    {
        lock (_disposeLock)
        {
            return _disposed;
        }
    }
}
