using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Channels;
using System.Windows;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class AppLogger : IAppLogger, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly Channel<AppLogEntry> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private readonly object _fileLock = new();
    private string? _currentLogFilePath;
    private string? _currentLogFolder;
    private int _currentLogFileLineCount;

    public AppLogger(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        UiLogs = [];
        _queue = Channel.CreateUnbounded<AppLogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _worker = Task.Run(ProcessQueueAsync);
    }

    public ObservableCollection<string> UiLogs { get; }

    public void Trace(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        => Enqueue(AppLogLevel.Trace, message, null, targets, filePath, memberName);

    public void Debug(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        => Enqueue(AppLogLevel.Debug, message, null, targets, filePath, memberName);

    public void Info(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        => Enqueue(AppLogLevel.Info, message, null, targets, filePath, memberName);

    public void Warning(string message, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        => Enqueue(AppLogLevel.Warning, message, null, targets, filePath, memberName);

    public void Error(string message, Exception? exception = null, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        => Enqueue(AppLogLevel.Error, message, exception, targets, filePath, memberName);

    public void Critical(string message, Exception? exception = null, LogTarget targets = LogTarget.All, string filePath = "", string memberName = "")
        => Enqueue(AppLogLevel.Critical, message, exception, targets, filePath, memberName);

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _shutdown.Cancel();

        try
        {
            _worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _shutdown.Dispose();
    }

    private void Enqueue(AppLogLevel level, string message, Exception? exception, LogTarget targets, string filePath, string memberName)
    {
        if (targets == LogTarget.None)
        {
            return;
        }

        var entry = new AppLogEntry
        {
            Level = level,
            Message = message,
            Targets = targets,
            SourceFileName = Path.GetFileName(filePath),
            MemberName = memberName,
            ExceptionText = exception?.ToString()
        };

        _queue.Writer.TryWrite(entry);
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync(_shutdown.Token))
            {
                var line = Format(entry);

                if (entry.Targets.HasFlag(LogTarget.File))
                {
                    WriteFileLog(line);
                }

                if (entry.Targets.HasFlag(LogTarget.Ui))
                {
                    WriteUiLog(line);
                }

                if (entry.Targets.HasFlag(LogTarget.Console))
                {
                    WriteConsoleLog(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string Format(AppLogEntry entry)
    {
        var line = $"[{entry.Timestamp.ToString(AppConstants.LogTimestampFormat)}] [{ToAbbreviation(entry.Level)}] [{entry.SourceFileName}] [{entry.MemberName}] {entry.Message}";
        return string.IsNullOrWhiteSpace(entry.ExceptionText) ? line : $"{line}{Environment.NewLine}{entry.ExceptionText}";
    }

    private void WriteFileLog(string line)
    {
        lock (_fileLock)
        {
            var filePath = GetCurrentLogFilePath();
            File.AppendAllText(filePath, line + Environment.NewLine);
            _currentLogFileLineCount += CountLines(line);
        }
    }

    private void WriteUiLog(string line)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            AddUiLogLine(line);
            return;
        }

        dispatcher.BeginInvoke(AddUiLogLine, line);
    }

    private static void WriteConsoleLog(string line)
    {
        Console.WriteLine(line);
        System.Diagnostics.Debug.WriteLine(line);
    }

    private string GetCurrentLogFilePath()
    {
        var folder = Path.Combine(_settingsService.Current.StateFolder, AppConstants.LogFolderName);
        if (!string.IsNullOrWhiteSpace(_currentLogFilePath) &&
            string.Equals(_currentLogFolder, folder, StringComparison.OrdinalIgnoreCase) &&
            _currentLogFileLineCount < AppConstants.MaxLogLinesPerFile)
        {
            return _currentLogFilePath;
        }

        Directory.CreateDirectory(folder);
        var timestamp = DateTime.Now.ToString(AppConstants.LogFileTimestampFormat);
        _currentLogFilePath = Path.Combine(folder, $"{timestamp}_{AppConstants.LogFileSuffix}{AppConstants.LogFileExtension}");
        _currentLogFolder = folder;
        _currentLogFileLineCount = 0;
        return _currentLogFilePath;
    }

    private void AddUiLogLine(string line)
    {
        UiLogs.Add(line);

        while (UiLogs.Count > AppConstants.MaxUiLogLines)
        {
            UiLogs.RemoveAt(0);
        }
    }

    private static string ToAbbreviation(AppLogLevel level)
    {
        return level switch
        {
            AppLogLevel.Trace => "TRC",
            AppLogLevel.Debug => "DBG",
            AppLogLevel.Info => "INF",
            AppLogLevel.Warning => "WRN",
            AppLogLevel.Error => "ERR",
            AppLogLevel.Critical => "CRT",
            _ => "LOG"
        };
    }

    private static int CountLines(string value)
    {
        if (value.Length == 0)
        {
            return 1;
        }

        return value.Count(c => c == '\n') + 1;
    }
}
