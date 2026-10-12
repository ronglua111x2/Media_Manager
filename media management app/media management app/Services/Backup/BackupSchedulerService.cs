using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services.Backup;

/// <summary>
/// Hybrid backup trigger: settings.json/Recipes changes are debounced (event-driven, since they only change on
/// deliberate user action), media-manager.db is throttled (it's written continuously by background tracking), and
/// a daily run acts as a safety net regardless of dirty-state detection. Detection is done by polling
/// LastWriteTimeUtc rather than hooking every write site, to keep the diff on existing services minimal.
/// </summary>
public sealed class BackupSchedulerService : IBackupSchedulerService
{
    private static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(8);

    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _databaseService;
    private readonly IBackupService _backupService;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _startLock = new();
    private readonly object _disposeLock = new();
    private Task? _worker;
    private bool _disposed;

    private DateTime? _settingsGroupLastWriteSeenUtc;
    private DateTime? _settingsGroupChangeDetectedAtUtc;
    private DateTime? _dbLastWriteSeenUtc;
    private bool _dbDirty;

    public BackupSchedulerService(
        ISettingsService settingsService,
        IDatabaseService databaseService,
        IBackupService backupService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;
        _backupService = backupService;
        _logger = logger;
    }

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
                    $"Backup scheduler did not stop within {ShutdownWaitTimeout.TotalSeconds:0} seconds.",
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
        using var timer = new PeriodicTimer(AppConstants.BackupPollInterval);
        try
        {
            do
            {
                await RunPollCycleSafelyAsync().ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunPollCycleSafelyAsync()
    {
        try
        {
            await RunPollCycleAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Backup scheduler poll cycle failed: {ex.Message}", LogTarget.File | LogTarget.Console);
        }
    }

    private async Task RunPollCycleAsync()
    {
        var backup = _settingsService.Current.Backup;
        if (!backup.Enabled)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;

        DetectSettingsGroupChange(nowUtc);
        DetectDbChange();

        if (IsSettingsDebounceElapsed(backup, nowUtc))
        {
            await RunAndResetAsync(BackupTriggerType.EventDriven, resetSettingsDebounce: true, resetDbDirty: false)
                .ConfigureAwait(false);
            return;
        }

        if (IsDbThrottleElapsed(backup, nowUtc))
        {
            await RunAndResetAsync(BackupTriggerType.EventDriven, resetSettingsDebounce: false, resetDbDirty: true)
                .ConfigureAwait(false);
            return;
        }

        if (IsDailySafetyNetDue(backup, nowUtc))
        {
            await RunAndResetAsync(BackupTriggerType.Daily, resetSettingsDebounce: true, resetDbDirty: true)
                .ConfigureAwait(false);
        }
    }

    private void DetectSettingsGroupChange(DateTime nowUtc)
    {
        var latestWrite = GetSettingsGroupLastWriteUtc();
        if (latestWrite is null)
        {
            return;
        }

        if (_settingsGroupLastWriteSeenUtc is null || latestWrite > _settingsGroupLastWriteSeenUtc)
        {
            _settingsGroupLastWriteSeenUtc = latestWrite;
            _settingsGroupChangeDetectedAtUtc = nowUtc;
        }
    }

    private void DetectDbChange()
    {
        var dbPath = _databaseService.DatabaseFilePath;
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
        {
            return;
        }

        var writeTimeUtc = File.GetLastWriteTimeUtc(dbPath);
        if (_dbLastWriteSeenUtc is null || writeTimeUtc > _dbLastWriteSeenUtc)
        {
            _dbLastWriteSeenUtc = writeTimeUtc;
            _dbDirty = true;
        }
    }

    private bool IsSettingsDebounceElapsed(BackupSettings backup, DateTime nowUtc)
    {
        if (_settingsGroupChangeDetectedAtUtc is null)
        {
            return false;
        }

        var debounce = TimeSpan.FromMinutes(backup.EventDebounceMinutes);
        return nowUtc - _settingsGroupChangeDetectedAtUtc.Value >= debounce;
    }

    private bool IsDbThrottleElapsed(BackupSettings backup, DateTime nowUtc)
    {
        if (!_dbDirty)
        {
            return false;
        }

        var throttle = TimeSpan.FromHours(backup.DbThrottleHours);
        return backup.LastBackupUtc is null || nowUtc - backup.LastBackupUtc.Value >= throttle;
    }

    private static bool IsDailySafetyNetDue(BackupSettings backup, DateTime nowUtc)
    {
        var localNow = nowUtc.ToLocalTime();
        if (localNow.Hour < backup.DailyBackupHour)
        {
            return false;
        }

        return backup.LastBackupUtc is null || backup.LastBackupUtc.Value.ToLocalTime().Date < localNow.Date;
    }

    private async Task RunAndResetAsync(BackupTriggerType triggerType, bool resetSettingsDebounce, bool resetDbDirty)
    {
        var result = await _backupService.RunBackupAsync(triggerType, _shutdown.Token).ConfigureAwait(false);
        if (result.Succeeded)
        {
            if (resetSettingsDebounce)
            {
                _settingsGroupChangeDetectedAtUtc = null;
            }

            if (resetDbDirty)
            {
                _dbDirty = false;
            }
        }
    }

    private DateTime? GetSettingsGroupLastWriteUtc()
    {
        DateTime? latest = null;

        var settingsPath = _settingsService.SettingsFilePath;
        if (File.Exists(settingsPath))
        {
            latest = File.GetLastWriteTimeUtc(settingsPath);
        }

        var recipesDir = Path.Combine(_settingsService.Current.StateFolder, AppConstants.BackupRecipesEntryFolderName);
        if (Directory.Exists(recipesDir))
        {
            foreach (var file in Directory.EnumerateFiles(recipesDir, "*.rcp"))
            {
                var writeTime = File.GetLastWriteTimeUtc(file);
                if (latest is null || writeTime > latest)
                {
                    latest = writeTime;
                }
            }
        }

        return latest;
    }
}
