using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using media_management_app.Common;

namespace media_management_app.Services;

public sealed class CrashLogService : ICrashLogService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly object _writeLock = new();
    private readonly object _aliveLock = new();
    private CancellationTokenSource? _heartbeatCts;
    private bool _disposed;
    private DateTime _sessionStartedUtc;

    public CrashLogService(ISettingsService settingsService, IAppLogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public void ReportUncleanShutdownIfNeeded()
    {
        var path = GetAliveFilePath();
        if (!File.Exists(path))
        {
            return;
        }

        SessionAliveState? previous = null;
        try
        {
            previous = JsonSerializer.Deserialize<SessionAliveState>(File.ReadAllText(path));
        }
        catch
        {
        }

        var note = previous is null
            ? "Previous session did not clear session.alive (unclean shutdown or native kill)."
            : $"Previous session pid={previous.Pid} startedUtc={previous.StartedUtc:o} lastBeatUtc={previous.LastBeatUtc:o} did not exit cleanly.";

        WriteCrashFile(
            kind: "UncleanShutdown",
            note: note,
            extra: null,
            exception: null);

        try
        {
            _logger.Error(note, targets: LogTarget.File | LogTarget.Console);
        }
        catch
        {
        }
    }

    public void MarkAlive()
    {
        lock (_aliveLock)
        {
            _sessionStartedUtc = DateTime.UtcNow;
            WriteAliveState();
            StartHeartbeatUnlocked();
        }
    }

    public void ClearAlive()
    {
        lock (_aliveLock)
        {
            StopHeartbeatUnlocked();
            TryDeleteFile(GetAliveFilePath());
        }
    }

    public void LogUnhandled(string kind, string note, Exception? exception = null, bool terminating = false)
    {
        var extra = terminating ? "Terminating=true" : null;
        WriteCrashFile(kind, note, extra, exception);

        try
        {
            if (exception is not null)
            {
                _logger.Critical(note, exception, LogTarget.File | LogTarget.Console);
            }
            else
            {
                _logger.Critical(note, targets: LogTarget.File | LogTarget.Console);
            }
        }
        catch
        {
        }
    }

    public void LogWebViewProcessFailed(string viewerName, CoreWebView2ProcessFailedEventArgs args, string? currentUrl)
    {
        var extra = $"""
            Viewer={viewerName}
            ProcessFailedKind={args.ProcessFailedKind}
            Reason={args.Reason}
            ExitCode={args.ExitCode}
            Url={currentUrl ?? "(none)"}
            """;

        WriteCrashFile(
            kind: "WebViewProcessFailed",
            note: $"{viewerName} WebView2 process failed ({args.ProcessFailedKind} / {args.Reason}).",
            extra: extra,
            exception: null);

        try
        {
            _logger.Error(
                $"{viewerName} WebView2 process failed. Kind={args.ProcessFailedKind}, Reason={args.Reason}, ExitCode={args.ExitCode}, Url={currentUrl ?? "(none)"}.",
                targets: LogTarget.File | LogTarget.Console);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearAlive();
    }

    private void StartHeartbeatUnlocked()
    {
        StopHeartbeatUnlocked();
        var cts = new CancellationTokenSource();
        _heartbeatCts = cts;
        _ = RunHeartbeatAsync(cts.Token);
    }

    private void StopHeartbeatUnlocked()
    {
        var cts = _heartbeatCts;
        _heartbeatCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(AppConstants.CrashHeartbeatInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                lock (_aliveLock)
                {
                    if (_heartbeatCts is null || _heartbeatCts.IsCancellationRequested)
                    {
                        return;
                    }

                    WriteAliveState();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void WriteAliveState()
    {
        try
        {
            var path = GetAliveFilePath();
            var state = new SessionAliveState
            {
                Pid = Environment.ProcessId,
                StartedUtc = _sessionStartedUtc == default ? DateTime.UtcNow : _sessionStartedUtc,
                LastBeatUtc = DateTime.UtcNow
            };
            File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch
        {
        }
    }

    private void WriteCrashFile(string kind, string note, string? extra, Exception? exception)
    {
        lock (_writeLock)
        {
            try
            {
                var folder = Path.Combine(_settingsService.Current.StateFolder, AppConstants.LogFolderName);
                Directory.CreateDirectory(folder);
                var stamp = DateTime.Now.ToString(AppConstants.LogFileTimestampFormat);
                var path = Path.Combine(
                    folder,
                    $"{stamp}_{AppConstants.CrashLogFileSuffix}{AppConstants.LogFileExtension}");

                var nowLocal = DateTime.Now;
                var nowUtc = DateTime.UtcNow;
                var builder = new StringBuilder();
                builder.AppendLine("=== Media Manager crash report ===");
                builder.AppendLine($"Kind: {kind}");
                builder.AppendLine($"TimeLocal: {nowLocal:yyyy-MM-dd HH:mm:ss.fff}");
                builder.AppendLine($"TimeUtc: {nowUtc:yyyy-MM-dd HH:mm:ss.fff}Z");
                builder.AppendLine($"Pid: {Environment.ProcessId}");
                builder.AppendLine($"Exe: {Environment.ProcessPath ?? AppContext.BaseDirectory}");
                builder.AppendLine($"OS: {Environment.OSVersion}");
                builder.AppendLine($"Note: {note}");
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    builder.AppendLine(extra.TrimEnd());
                }

                if (exception is not null)
                {
                    builder.AppendLine("Exception:");
                    builder.AppendLine(exception.ToString());
                }

                File.WriteAllText(path, builder.ToString());
            }
            catch
            {
            }
        }
    }

    private string GetAliveFilePath()
        => Path.Combine(_settingsService.Current.StateFolder, AppConstants.SessionAliveFileName);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class SessionAliveState
    {
        public int Pid { get; set; }

        public DateTime StartedUtc { get; set; }

        public DateTime LastBeatUtc { get; set; }
    }
}
