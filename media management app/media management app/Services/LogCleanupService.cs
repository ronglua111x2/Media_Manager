using media_management_app.Common;

namespace media_management_app.Services;

public sealed class LogCleanupService : ILogCleanupService, IDisposable
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);

    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _startLock = new();
    private Task? _worker;

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

            if (!IsManagedLogFile(filePath) || IsActiveLogFile(filePath, activeLogFilePath))
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
        var baseSuffix = $"_{AppConstants.LogFileSuffix}";
        if (fileNameWithoutExtension.EndsWith(baseSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rotationSuffixPrefix = $"{baseSuffix}_";
        var rotationSuffixIndex = fileNameWithoutExtension.LastIndexOf(rotationSuffixPrefix, StringComparison.OrdinalIgnoreCase);
        if (rotationSuffixIndex < 0)
        {
            return false;
        }

        var rotationNumber = fileNameWithoutExtension[(rotationSuffixIndex + rotationSuffixPrefix.Length)..];
        return int.TryParse(rotationNumber, out var parsed) && parsed > 0;
    }

    private static bool IsActiveLogFile(string filePath, string? activeLogFilePath)
    {
        return !string.IsNullOrWhiteSpace(activeLogFilePath) &&
            string.Equals(
                Path.GetFullPath(filePath),
                Path.GetFullPath(activeLogFilePath),
                StringComparison.OrdinalIgnoreCase);
    }
}
