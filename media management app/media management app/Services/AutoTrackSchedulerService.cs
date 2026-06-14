using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class AutoTrackSchedulerService : IAutoTrackSchedulerService
{
    private readonly ISettingsService _settingsService;
    private readonly IAutoTrackService _autoTrackService;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _startLock = new();
    private Task? _worker;

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
        _shutdown.Cancel();

        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _shutdown.Dispose();
    }

    private async Task RunAsync()
    {
        await RunSafelyAsync();

        while (!_shutdown.Token.IsCancellationRequested)
        {
            var interval = GetInterval();
            try
            {
                await Task.Delay(interval, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunSafelyAsync();
        }
    }

    private TimeSpan GetInterval()
    {
        var hours = Math.Clamp(_settingsService.Current.AutoTrack?.IntervalHours ?? 6, 1, 168);
        return TimeSpan.FromHours(hours);
    }

    private async Task RunSafelyAsync()
    {
        if (!(_settingsService.Current.AutoTrack?.Enabled ?? true))
        {
            return;
        }

        try
        {
            var result = await _autoTrackService.RunAsync(_shutdown.Token);
            RunCompleted?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning($"Auto-track scheduler run failed: {ex.Message}", LogTarget.All);
        }
    }
}
