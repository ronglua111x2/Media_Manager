using media_management_app.Common;

namespace media_management_app.Services;

/// <summary>
/// Tails Cloudflare WARP logs at EOF and raises a hint when a status-like line appears.
/// The hint only wakes a warp-cli confirm — it is not treated as source of truth.
/// </summary>
public sealed class WarpLogStatusWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HintDebounce = TimeSpan.FromSeconds(1);

    private readonly IAppLifecycleService _lifecycleService;
    private readonly IAppLogger _logger;
    private readonly Func<CancellationToken, Task> _onStatusHint;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();

    private AppendOnlyLogTailer? _tailer;
    private FileSystemWatcher? _watcher;
    private Task? _pollTask;
    private int _hintVersion;
    private bool _disposed;
    private bool _pollRunning;

    public WarpLogStatusWatcher(
        IAppLifecycleService lifecycleService,
        IAppLogger logger,
        Func<CancellationToken, Task> onStatusHint)
    {
        _lifecycleService = lifecycleService;
        _logger = logger;
        _onStatusHint = onStatusHint;
    }

    public void Start()
    {
        TryOpenTailer();
        TryStartDirectoryWatcher();
        _lifecycleService.AppModeChanged += OnAppModeChanged;
        if (_lifecycleService.CurrentMode != AppMode.Background)
        {
            StartPollLoop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifecycleService.AppModeChanged -= OnAppModeChanged;
        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        StopPollLoop();
        DisposeWatcher();
        DisposeTailer();
        try
        {
            _shutdown.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnAppModeChanged(object? sender, AppMode mode)
    {
        if (mode == AppMode.Background)
        {
            StopPollLoop();
            return;
        }

        StartPollLoop();
    }

    private void StartPollLoop()
    {
        lock (_gate)
        {
            if (_disposed || _pollRunning)
            {
                return;
            }

            _pollRunning = true;
            _pollTask = Task.Run(() => PollAsync(_shutdown.Token));
        }
    }

    private void StopPollLoop()
    {
        lock (_gate)
        {
            _pollRunning = false;
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool running;
            lock (_gate)
            {
                running = _pollRunning && !_disposed;
            }

            if (!running)
            {
                break;
            }

            try
            {
                if (ReadStatusHint())
                {
                    QueueHint();
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"WARP log tail poll failed: {ex.Message}", LogTarget.File);
            }

            try
            {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private bool ReadStatusHint()
    {
        AppendOnlyLogTailer? tailer;
        lock (_gate)
        {
            tailer = _tailer;
        }

        if (tailer is null)
        {
            TryOpenTailer();
            return false;
        }

        try
        {
            foreach (var line in tailer.ReadNewLines())
            {
                if (LooksLikeStatusLine(line))
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            DisposeTailer();
            TryOpenTailer();
        }

        return false;
    }

    private void TryOpenTailer()
    {
        lock (_gate)
        {
            if (_tailer is not null || _disposed)
            {
                return;
            }
        }

        var path = TryFindNewestLogFile();
        if (path is null)
        {
            return;
        }

        if (!AppendOnlyLogTailer.TryOpen(path, out var tailer, out var error))
        {
            _logger.Debug($"WARP log tail skipped: {error}", LogTarget.File);
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                tailer!.Dispose();
                return;
            }

            _tailer?.Dispose();
            _tailer = tailer;
        }

        _logger.Debug($"WARP log tail opened at EOF: {path}", LogTarget.File | LogTarget.Console);
    }

    private void TryStartDirectoryWatcher()
    {
        var dir = AppConstants.DefaultWarpLogDirectory;
        if (!Directory.Exists(dir))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnLogDirectoryChanged;
            watcher.Created += OnLogDirectoryChanged;
            lock (_gate)
            {
                if (_disposed)
                {
                    watcher.Dispose();
                    return;
                }

                _watcher = watcher;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"WARP log directory watcher skipped: {ex.Message}", LogTarget.File);
        }
    }

    private void OnLogDirectoryChanged(object sender, FileSystemEventArgs e) => QueueHint();

    private void QueueHint()
    {
        var version = Interlocked.Increment(ref _hintVersion);
        _ = DebouncedHintAsync(version);
    }

    private async Task DebouncedHintAsync(int version)
    {
        try
        {
            await Task.Delay(HintDebounce, _shutdown.Token);
            if (version != Volatile.Read(ref _hintVersion) || _disposed)
            {
                return;
            }

            await _onStatusHint(_shutdown.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Debug($"WARP log-tail status hint failed: {ex.Message}", LogTarget.File);
        }
    }

    private static string? TryFindNewestLogFile()
    {
        var dir = AppConstants.DefaultWarpLogDirectory;
        if (!Directory.Exists(dir))
        {
            return null;
        }

        try
        {
            return new DirectoryInfo(dir)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Where(IsWarpLogFile)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsWarpLogFile(FileInfo file)
    {
        var ext = file.Extension;
        if (ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
               file.Name.Contains("log", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool LooksLikeStatusLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        return line.Contains("Disconnected", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Connected", StringComparison.OrdinalIgnoreCase);
    }

    private void DisposeTailer()
    {
        lock (_gate)
        {
            _tailer?.Dispose();
            _tailer = null;
        }
    }

    private void DisposeWatcher()
    {
        FileSystemWatcher? watcher;
        lock (_gate)
        {
            watcher = _watcher;
            _watcher = null;
        }

        if (watcher is null)
        {
            return;
        }

        watcher.Changed -= OnLogDirectoryChanged;
        watcher.Created -= OnLogDirectoryChanged;
        watcher.Dispose();
    }
}
