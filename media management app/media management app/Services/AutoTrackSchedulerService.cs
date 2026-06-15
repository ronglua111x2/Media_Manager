using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class AutoTrackSchedulerService : IAutoTrackSchedulerService
{
    private static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(8);

    private readonly ISettingsService _settingsService;
    private readonly IAutoTrackService _autoTrackService;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _startLock = new();
    private readonly object _disposeLock = new();
    private Task? _worker;
    private bool _disposed;

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
        await Task.WhenAll(
            RunTmdbDiscoveryLoopAsync(),
            RunTorrentHuntLoopAsync(),
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

    private async Task RunTorrentHuntLoopAsync()
    {
        if (IsDisposed())
        {
            return;
        }

        await RunTorrentHuntSafelyAsync();

        while (!IsDisposed() && !_shutdown.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(GetHuntInterval(), _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunTorrentHuntSafelyAsync();
        }
    }

    private async Task RunReconcileLoopAsync()
    {
        if (IsDisposed())
        {
            return;
        }

        await RunReconcileSafelyAsync();

        while (!IsDisposed() && !_shutdown.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(GetReconcileInterval(), _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunReconcileSafelyAsync();
        }
    }

    private TimeSpan GetTmdbInterval()
    {
        var minutes = Math.Clamp(_settingsService.Current.AutoTrack?.TmdbCheckIntervalMinutes ?? 30, 5, 1440);
        return TimeSpan.FromMinutes(minutes);
    }

    private TimeSpan GetHuntInterval()
    {
        var minutes = Math.Clamp(_settingsService.Current.AutoTrack?.TorrentHuntIntervalMinutes ?? 60, 15, 1440);
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

        _logger.Info("Auto-track TMDB discovery cycle starting.", LogTarget.All);

        try
        {
            var result = await _autoTrackService.RunTmdbDiscoveryAsync(cancellationToken: _shutdown.Token);
            _logger.Info($"Auto-track TMDB discovery cycle complete. {result.Summary}", LogTarget.All);
            NotifyRunCompleted(result);
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

    private async Task RunTorrentHuntSafelyAsync()
    {
        if (!(_settingsService.Current.AutoTrack?.Enabled ?? true))
        {
            return;
        }

        _logger.Info("Auto-track torrent hunt cycle starting.", LogTarget.All);

        try
        {
            var result = await _autoTrackService.RunTorrentHuntAsync(_shutdown.Token);
            _logger.Info($"Auto-track torrent hunt cycle complete. {result.Summary}", LogTarget.All);
            _autoTrackService.RecordRunResult(result);
            NotifyRunCompleted(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var result = FailedResult($"Torrent hunt failed: {ex.Message}");
            _logger.Warning($"Auto-track torrent hunt loop failed: {ex.Message}", LogTarget.All);
            _autoTrackService.RecordRunResult(result);
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
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning($"Auto-track reconcile loop failed: {ex.Message}", LogTarget.All);
        }
    }

    private void NotifyRunCompleted(AutoTrackRunResult result)
    {
        RunCompleted?.Invoke(this, result);
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
