using System.Diagnostics;
using System.Text;
using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class WarpCliService : IWarpCliService, IDisposable
{
    private const int StatusPollIntervalMilliseconds = 2000;

    private readonly ISettingsService _settingsService;
    private readonly IWindowsNotificationService _windowsNotificationService;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _cliGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly HashSet<WarpLeaseReason> _leases = [];
    private readonly WarpLogStatusWatcher _logWatcher;

    private bool _inFlight;
    private bool _lastKnownConnected;
    private bool _disposed;

    public WarpCliService(
        ISettingsService settingsService,
        IWindowsNotificationService windowsNotificationService,
        IAppLifecycleService lifecycleService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _windowsNotificationService = windowsNotificationService;
        _logger = logger;
        _logWatcher = new WarpLogStatusWatcher(lifecycleService, logger, ConfirmExternalStatusAsync);
        _logWatcher.Start();
    }

    public event EventHandler<WarpConnectionChangedEventArgs>? ConnectionChanged;

    public string ResolvedExecutablePath
    {
        get
        {
            var configured = _settingsService.Current.Warp?.ExecutablePath?.Trim();
            return string.IsNullOrWhiteSpace(configured)
                ? AppConstants.DefaultWarpCliPath
                : configured;
        }
    }

    public bool IsAvailable => File.Exists(ResolvedExecutablePath);

    public bool InFlight
    {
        get
        {
            lock (_stateLock)
            {
                return _inFlight;
            }
        }
    }

    public bool LastKnownConnected
    {
        get
        {
            lock (_stateLock)
            {
                return _lastKnownConnected;
            }
        }
    }

    public IReadOnlySet<WarpLeaseReason> ActiveLeases
    {
        get
        {
            lock (_stateLock)
            {
                return new HashSet<WarpLeaseReason>(_leases);
            }
        }
    }

    public bool HasAutoTrackPipelineLease
    {
        get
        {
            lock (_stateLock)
            {
                return _leases.Any(WarpLeaseReasonText.IsAutoTrackPipeline);
            }
        }
    }

    public async Task<WarpLeaseResult> AcquireAsync(
        WarpLeaseReason reason,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        await _cliGate.WaitAsync(ct);
        try
        {
            if (!IsAvailable)
            {
                _logger.Warning($"WARP CLI not available at {ResolvedExecutablePath}.", LogTarget.All);
                return new WarpLeaseResult(false, false);
            }

            lock (_stateLock)
            {
                if (_leases.Contains(reason))
                {
                    return new WarpLeaseResult(_lastKnownConnected, false);
                }
            }

            SetInFlight(true, WarpConnectionChangeSource.Cli);

            var alreadyConnected = await QueryConnectedUnlockedAsync(ct);
            if (alreadyConnected)
            {
                AddLease(reason);
                _logger.Debug(
                    $"WARP lease acquired ({WarpLeaseReasonText.Label(reason)}); tunnel already up.",
                    LogTarget.File | LogTarget.Console);
                RaiseChanged(WarpConnectionChangeSource.Cli);
                return new WarpLeaseResult(true, false);
            }

            _logger.Info($"Connecting WARP via warp-cli ({WarpLeaseReasonText.Label(reason)})...", LogTarget.All);
            await RunCliUnlockedAsync("connect", ct);
            var connected = await WaitUntilConnectedUnlockedAsync(timeout, ct);
            if (!connected)
            {
                _logger.Warning($"WARP connect timed out after {timeout.TotalSeconds:0}s.", LogTarget.All);
                SetLastKnown(false);
                RaiseChanged(WarpConnectionChangeSource.Cli);
                return new WarpLeaseResult(false, false);
            }

            AddLease(reason);
            SetLastKnown(true);
            _logger.Info($"WARP connected ({WarpLeaseReasonText.Label(reason)}).", LogTarget.All);
            ShowConnectedToast();
            RaiseChanged(WarpConnectionChangeSource.Cli);
            return new WarpLeaseResult(true, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP connect failed: {ex.Message}", LogTarget.All);
            return new WarpLeaseResult(false, false);
        }
        finally
        {
            SetInFlight(false, WarpConnectionChangeSource.Cli);
            _cliGate.Release();
        }
    }

    public async Task ReleaseAsync(WarpLeaseReason reason, CancellationToken ct = default)
    {
        await _cliGate.WaitAsync(ct);
        try
        {
            bool removed;
            int remaining;
            lock (_stateLock)
            {
                removed = _leases.Remove(reason);
                remaining = _leases.Count;
            }

            if (!removed)
            {
                _logger.Debug(
                    $"WARP lease release ignored ({WarpLeaseReasonText.Label(reason)}); not held.",
                    LogTarget.File | LogTarget.Console);
                return;
            }

            if (remaining > 0)
            {
                _logger.Debug(
                    $"WARP lease released ({WarpLeaseReasonText.Label(reason)}); {remaining} lease(s) remain.",
                    LogTarget.File | LogTarget.Console);
                RaiseChanged(WarpConnectionChangeSource.Cli);
                return;
            }

            await DisconnectCliUnlockedAsync(ct, logAsForce: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP lease release failed ({WarpLeaseReasonText.Label(reason)}): {ex.Message}", LogTarget.All);
        }
        finally
        {
            _cliGate.Release();
        }
    }

    public async Task ForceDisconnectAsync(CancellationToken ct = default)
    {
        await _cliGate.WaitAsync(ct);
        try
        {
            SetInFlight(true, WarpConnectionChangeSource.User);
            string remaining;
            lock (_stateLock)
            {
                remaining = WarpLeaseReasonText.Describe(_leases);
                _leases.Clear();
            }

            if (!string.IsNullOrEmpty(remaining))
            {
                _logger.Warning(
                    $"WARP force-disconnect while leases were active: {remaining}.",
                    LogTarget.All);
            }

            await DisconnectCliUnlockedAsync(ct, logAsForce: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP force-disconnect failed: {ex.Message}", LogTarget.All);
        }
        finally
        {
            SetInFlight(false, WarpConnectionChangeSource.User);
            _cliGate.Release();
        }
    }

    public async Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var result = await AcquireAsync(WarpLeaseReason.User, timeout, ct);
        return result.Connected;
    }

    public async Task<WarpConnectAttempt> ConnectOwnedAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var result = await AcquireAsync(WarpLeaseReason.User, timeout, ct);
        return new WarpConnectAttempt(result.Connected, result.StartedByThisAcquire);
    }

    public Task DisconnectAsync(CancellationToken ct = default) => ForceDisconnectAsync(ct);

    public async Task<bool> IsConnectedAsync(CancellationToken ct = default)
    {
        await _cliGate.WaitAsync(ct);
        try
        {
            var connected = await QueryConnectedUnlockedAsync(ct);
            var previous = LastKnownConnected;
            SetLastKnown(connected);
            if (previous != connected)
            {
                RaiseChanged(WarpConnectionChangeSource.Poll);
            }

            return connected;
        }
        finally
        {
            _cliGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logWatcher.Dispose();
        _cliGate.Dispose();
    }

    private async Task ConfirmExternalStatusAsync(CancellationToken ct)
    {
        if (_disposed || !IsAvailable)
        {
            return;
        }

        var entered = await _cliGate.WaitAsync(0, ct);
        if (!entered)
        {
            return;
        }

        try
        {
            var connected = await QueryConnectedUnlockedAsync(ct);
            bool changed;
            lock (_stateLock)
            {
                changed = _lastKnownConnected != connected;
                _lastKnownConnected = connected;
            }

            if (changed)
            {
                _logger.Info(
                    connected
                        ? "WARP status flipped to connected (log-tail confirm)."
                        : "WARP status flipped to disconnected (log-tail confirm).",
                    LogTarget.All);
                RaiseChanged(WarpConnectionChangeSource.LogTail);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Debug($"WARP log-tail confirm failed: {ex.Message}", LogTarget.File);
        }
        finally
        {
            _cliGate.Release();
        }
    }

    private async Task<bool> WaitUntilConnectedUnlockedAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await QueryConnectedUnlockedAsync(ct))
            {
                return true;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Min(StatusPollIntervalMilliseconds, remaining.TotalMilliseconds)),
                ct);
        }

        return false;
    }

    private async Task DisconnectCliUnlockedAsync(CancellationToken ct, bool logAsForce)
    {
        if (!IsAvailable)
        {
            _logger.Warning($"WARP CLI not available at {ResolvedExecutablePath}.", LogTarget.All);
            SetLastKnown(false);
            RaiseChanged(logAsForce ? WarpConnectionChangeSource.User : WarpConnectionChangeSource.Cli);
            return;
        }

        try
        {
            _logger.Info(
                logAsForce ? "Force-disconnecting WARP via warp-cli..." : "Disconnecting WARP via warp-cli...",
                LogTarget.All);
            var result = await RunCliUnlockedAsync("disconnect", ct);
            _logger.Info(
                $"WARP disconnect completed (exit {result.ExitCode}). Output: {result.Output.Trim()}",
                LogTarget.File | LogTarget.Console);
            SetLastKnown(false);
            ShowDisconnectedToast();
            RaiseChanged(logAsForce ? WarpConnectionChangeSource.User : WarpConnectionChangeSource.Cli);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP disconnect failed: {ex.Message}", LogTarget.All);
        }
    }

    private async Task<bool> QueryConnectedUnlockedAsync(CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return false;
        }

        try
        {
            var jsonResult = await RunCliUnlockedAsync("status --json", ct);
            if (ParseConnectedStatus(jsonResult.Output))
            {
                return true;
            }

            var textResult = await RunCliUnlockedAsync("status", ct);
            return ParseConnectedStatus(textResult.Output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP status check failed: {ex.Message}", LogTarget.File);
            return false;
        }
    }

    private async Task<CliRunResult> RunCliUnlockedAsync(string arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolvedExecutablePath,
            Arguments = $"--accept-tos {arguments}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                outputBuilder.AppendLine(e.Data);
                _logger.Debug($"[warp-cli] {e.Data}", LogTarget.File);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                outputBuilder.AppendLine(e.Data);
                _logger.Debug($"[warp-cli stderr] {e.Data}", LogTarget.File);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);

        return new CliRunResult(process.ExitCode, outputBuilder.ToString());
    }

    private void AddLease(WarpLeaseReason reason)
    {
        lock (_stateLock)
        {
            _leases.Add(reason);
        }
    }

    private void SetLastKnown(bool connected)
    {
        lock (_stateLock)
        {
            _lastKnownConnected = connected;
        }
    }

    private void SetInFlight(bool inFlight, WarpConnectionChangeSource source)
    {
        bool changed;
        lock (_stateLock)
        {
            changed = _inFlight != inFlight;
            _inFlight = inFlight;
        }

        if (changed)
        {
            RaiseChanged(source);
        }
    }

    private void RaiseChanged(WarpConnectionChangeSource source)
    {
        WarpConnectionChangedEventArgs args;
        lock (_stateLock)
        {
            args = new WarpConnectionChangedEventArgs
            {
                IsConnected = _lastKnownConnected,
                Leases = new HashSet<WarpLeaseReason>(_leases),
                InFlight = _inFlight,
                Source = source
            };
        }

        ConnectionChanged?.Invoke(this, args);
    }

    private void ShowConnectedToast()
    {
        _windowsNotificationService.TryShow(new WindowsNotificationRequest
        {
            Title = "WARP",
            Message = "Connected.",
            Kind = NotificationKind.WarpRecovered
        });
    }

    private void ShowDisconnectedToast()
    {
        _windowsNotificationService.TryShow(new WindowsNotificationRequest
        {
            Title = "WARP",
            Message = "Disconnected.",
            Kind = NotificationKind.WarpDisconnected
        });
    }

    internal static bool ParseConnectedStatus(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        if (output.Contains("Disconnected", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (output.Contains("Connected", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            return ContainsConnectedStatus(document.RootElement);
        }
        catch (JsonException)
        {
            return output.Contains("Connected", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool ContainsConnectedStatus(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return string.Equals(element.GetString(), "Connected", StringComparison.OrdinalIgnoreCase);
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ContainsConnectedStatus(property.Value))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsConnectedStatus(item))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private sealed record CliRunResult(int ExitCode, string Output);
}
