using System.Text.RegularExpressions;
using media_management_app.Common;

namespace media_management_app.Services;

public sealed class LogCleanupService : ILogCleanupService, IDisposable
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly Regex SessionLogFileName = new(
        @"^\d{8}_\d{6}_\d{3}_[A-Za-z0-9]+(?:_[1-9]\d*)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LegacyCartDebugFileName = new(
        @"^cart-debug_\d{8}_\d{6}_\d{3}(?:_[1-9]\d*)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _startLock = new();
    private readonly object _disposeLock = new();
    private Task? _worker;
    private bool _disposed;

    public LogCleanupService(ISettingsService settingsService, IAppLogger logger)
    {
        _settingsService = settingsService;
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
                    $"Log cleanup service did not stop within {ShutdownWaitTimeout.TotalSeconds:0} seconds.",
                    LogTarget.File | LogTarget.Console);
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
        await RunCleanupSafelyAsync();

        using var timer = new PeriodicTimer(CleanupInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token))
            {
                await RunCleanupSafelyAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task RunCleanupSafelyAsync()
    {
        try
        {
            CleanupLogFolder();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Log cleanup failed: {ex.Message}", LogTarget.File | LogTarget.Console);
        }

        return Task.CompletedTask;
    }

    private void CleanupLogFolder()
    {
        var logFolder = Path.Combine(_settingsService.Current.StateFolder, AppConstants.LogFolderName);
        if (!Directory.Exists(logFolder))
        {
            return;
        }

        var retentionDays = Math.Clamp(
            _settingsService.Current.Logs.CleanupRetentionDays,
            AppConstants.MinLogCleanupRetentionDays,
            AppConstants.MaxLogCleanupRetentionDays);
        var cutoff = DateTime.Now.AddDays(-retentionDays);
        var activeLogFilePath = _logger.ActiveLogFilePath;
        var deletedCount = 0;

        foreach (var filePath in Directory.EnumerateFiles(logFolder))
        {
            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            if (!IsManagedLogFile(filePath) || IsProtectedLogFile(filePath, activeLogFilePath))
            {
                continue;
            }

            try
            {
                if (File.GetCreationTime(filePath) >= cutoff)
                {
                    continue;
                }

                File.Delete(filePath);
                deletedCount++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning($"Could not delete old log file '{filePath}': {ex.Message}", LogTarget.File | LogTarget.Console);
            }
        }

        if (deletedCount > 0)
        {
            _logger.Info($"Log cleanup deleted {deletedCount} old log file(s).", LogTarget.File | LogTarget.Console);
        }
    }

    private static bool IsManagedLogFile(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), AppConstants.LogFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        return SessionLogFileName.IsMatch(fileNameWithoutExtension)
            || LegacyCartDebugFileName.IsMatch(fileNameWithoutExtension);
    }

    private bool IsProtectedLogFile(string filePath, string? activeLogFilePath)
    {
        return IsSamePath(filePath, activeLogFilePath)
            || IsSamePath(filePath, _settingsService.ActiveSettingsLogFilePath);
    }

    private static bool IsSamePath(string filePath, string? otherPath)
    {
        return !string.IsNullOrWhiteSpace(otherPath) &&
            string.Equals(
                Path.GetFullPath(filePath),
                Path.GetFullPath(otherPath),
                StringComparison.OrdinalIgnoreCase);
    }
}
