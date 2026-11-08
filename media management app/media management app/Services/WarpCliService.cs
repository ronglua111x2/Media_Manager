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
    // Guarded by _stateLock: held leases that asked for a tunnel. Decided at acquire, so changing a setting
    // mid-hunt cannot switch the mode back under a running search.
    private readonly HashSet<WarpLeaseReason> _tunnelLeases = [];
    private readonly WarpLogStatusWatcher _logWatcher;

    private bool _inFlight;
    private bool _lastKnownConnected;
    // True when this app connected the tunnel; a tunnel already up before the first lease is left connected on release.
    private bool _tunnelOwnedByApp;
    // Guarded by _cliGate: the user's mode before the app switched to WarpModePolicy.AppTunnelMode; null when not switched.
    private string? _modeSwitchedFrom;
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
                    return new WarpLeaseResult(_lastKnownConnected, false, _tunnelLeases.Contains(reason));
                }
            }

            SetInFlight(true, WarpConnectionChangeSource.Cli);

            var alreadyConnected = await QueryConnectedUnlockedAsync(ct);
            var (modeSwitched, tunnelActive) = await EnsureTunnelModeUnlockedAsync(reason, ct);
            if (alreadyConnected && modeSwitched &&
                !await WaitUntilConnectedUnlockedAsync(timeout, ct))
            {
                // The mode change reconnects an existing tunnel; it did not come back.
                _logger.Warning(
                    $"WARP did not reconnect within {timeout.TotalSeconds:0}s after the mode switch ({WarpLeaseReasonText.Label(reason)}).",
                    LogTarget.All);
                await RestoreModeIfUnneededUnlockedAsync(ct);
                SetLastKnown(false);
                RaiseChanged(WarpConnectionChangeSource.Cli);
                return new WarpLeaseResult(false, false);
            }

            if (alreadyConnected)
            {
                lock (_stateLock)
                {
                    if (_leases.Count == 0)
                    {
                        _tunnelOwnedByApp = false;
                    }
                }

                AddLease(reason, tunnelActive);
                _logger.Debug(
                    $"WARP lease acquired ({WarpLeaseReasonText.Label(reason)}); tunnel already up.",
                    LogTarget.File | LogTarget.Console);
                RaiseChanged(WarpConnectionChangeSource.Cli);
                return new WarpLeaseResult(true, false, tunnelActive);
            }

            _logger.Info($"Connecting WARP via warp-cli ({WarpLeaseReasonText.Label(reason)})...", LogTarget.All);
            var connectResult = await RunCliUnlockedAsync("connect", ct);
            if (connectResult.ExitCode != 0)
            {
                LogCliOutputOnce("connect", connectResult);
            }

            var connected = await WaitUntilConnectedUnlockedAsync(timeout, ct);
            if (!connected)
            {
                _logger.Warning($"WARP connect timed out after {timeout.TotalSeconds:0}s.", LogTarget.All);
                await RestoreModeIfUnneededUnlockedAsync(ct);
                SetLastKnown(false);
                RaiseChanged(WarpConnectionChangeSource.Cli);
                return new WarpLeaseResult(false, false);
            }

            lock (_stateLock)
            {
                _tunnelOwnedByApp = true;
            }

            AddLease(reason, tunnelActive);
            SetLastKnown(true);
            _logger.Info($"WARP connected ({WarpLeaseReasonText.Label(reason)}).", LogTarget.All);
            ShowConnectedToast();
            RaiseChanged(WarpConnectionChangeSource.Cli);
            return new WarpLeaseResult(true, true, tunnelActive);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP connect failed: {ex.Message}", LogTarget.All);
            await RestoreModeIfUnneededUnlockedAsync(CancellationToken.None);
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
            bool ownedByApp;
            lock (_stateLock)
            {
                removed = _leases.Remove(reason);
                _tunnelLeases.Remove(reason);
                remaining = _leases.Count;
                ownedByApp = _tunnelOwnedByApp;
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
                await RestoreModeIfUnneededUnlockedAsync(ct);
                RaiseChanged(WarpConnectionChangeSource.Cli);
                return;
            }

            if (!ownedByApp)
            {
                _logger.Debug(
                    $"WARP lease released ({WarpLeaseReasonText.Label(reason)}); tunnel was already up before acquire — leaving connected.",
                    LogTarget.File | LogTarget.Console);
                await RestoreModeIfUnneededUnlockedAsync(ct);
                RaiseChanged(WarpConnectionChangeSource.Cli);
                return;
            }

            await DisconnectCliUnlockedAsync(ct, logAsForce: false);
            await RestoreModeIfUnneededUnlockedAsync(ct);
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
                _tunnelLeases.Clear();
                _tunnelOwnedByApp = false;
            }

            if (!string.IsNullOrEmpty(remaining))
            {
                _logger.Warning(
                    $"WARP force-disconnect while leases were active: {remaining}.",
                    LogTarget.All);
            }

            await DisconnectCliUnlockedAsync(ct, logAsForce: true);
            await RestoreModeIfUnneededUnlockedAsync(ct);
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

    public async Task<string?> GetOperationModeAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return null;
        }

        await _cliGate.WaitAsync(ct);
        try
        {
            return await QueryModeUnlockedAsync(ct);
        }
        finally
        {
            _cliGate.Release();
        }
    }

    public async Task RestorePendingModeAsync(CancellationToken ct = default)
    {
        if (!IsAvailable || !File.Exists(MarkerPath))
        {
            return;
        }

        await _cliGate.WaitAsync(ct);
        try
        {
            lock (_stateLock)
            {
                if (_leases.Count > 0)
                {
                    return;
                }
            }

            var marker = TryReadMarker();
            if (marker is null)
            {
                _logger.Warning($"WARP mode restore marker unreadable; deleting {MarkerPath}.", LogTarget.All);
                TryDeleteMarker();
                return;
            }

            _logger.Info(
                $"WARP mode was switched to {marker.AppliedMode} by a previous session (since {marker.AppliedUtc:u}); restoring {marker.OriginalMode}.",
                LogTarget.All);
            await RestoreFromMarkerUnlockedAsync(marker, "after unclean exit", ct);
            _modeSwitchedFrom = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP mode restore after unclean exit failed: {ex.Message}", LogTarget.All);
        }
        finally
        {
            _cliGate.Release();
        }
    }

    private string MarkerPath => Path.Combine(_settingsService.Current.StateFolder, WarpModePolicy.MarkerFileName);

    /// <summary>
    /// Switches a DNS-only mode to <see cref="WarpModePolicy.AppTunnelMode"/> for leases that need a real tunnel.
    /// Switched: this call changed the mode. TunnelActive: the lease asked for a tunnel and the mode provides one.
    /// </summary>
    private async Task<(bool Switched, bool TunnelActive)> EnsureTunnelModeUnlockedAsync(WarpLeaseReason reason, CancellationToken ct)
    {
        if (!WarpModePolicy.LeaseNeedsTunnel(reason, _settingsService.Current.Warp))
        {
            return (false, false);
        }

        if (_modeSwitchedFrom is not null)
        {
            return (false, true);
        }

        var mode = await QueryModeUnlockedAsync(ct);
        if (mode is null)
        {
            _logger.Warning(
                $"WARP mode could not be read; not switching it for {WarpLeaseReasonText.Label(reason)}.",
                LogTarget.All);
            return (false, false);
        }

        if (WarpModePolicy.IsTunnelMode(mode))
        {
            return (false, true);
        }

        if (!WarpModePolicy.IsValidModeName(mode))
        {
            _logger.Warning($"WARP mode '{mode}' is not recognised; not switching it.", LogTarget.All);
            return (false, false);
        }

        // Marker first: if the app dies before restoring, the next start puts the user's mode back.
        TryWriteMarker(new WarpModeRestoreMarker(mode, WarpModePolicy.AppTunnelMode, DateTime.UtcNow));
        var result = await RunCliUnlockedAsync($"mode {WarpModePolicy.AppTunnelMode}", ct);
        if (result.ExitCode != 0)
        {
            LogCliOutputOnce("mode", result);
            TryDeleteMarker();
            _logger.Warning(
                $"WARP mode switch {mode} → {WarpModePolicy.AppTunnelMode} failed for {WarpLeaseReasonText.Label(reason)}; no tunnel.",
                LogTarget.All);
            return (false, false);
        }

        _modeSwitchedFrom = mode;
        _logger.Info(
            $"WARP mode {mode} → {WarpModePolicy.AppTunnelMode} for {WarpLeaseReasonText.Label(reason)} (DNS-only modes cannot reach TMDB or most search engines); your mode is restored when done.",
            LogTarget.All);
        return (true, true);
    }

    /// <summary>Restores the user's mode once no held lease needs the tunnel.</summary>
    private async Task RestoreModeIfUnneededUnlockedAsync(CancellationToken ct)
    {
        var original = _modeSwitchedFrom;
        if (original is null)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_tunnelLeases.Count > 0)
            {
                return;
            }
        }

        await RestoreFromMarkerUnlockedAsync(
            new WarpModeRestoreMarker(original, WarpModePolicy.AppTunnelMode, DateTime.UtcNow),
            "tunnel no longer needed",
            ct);
        _modeSwitchedFrom = null;
    }

    private async Task RestoreFromMarkerUnlockedAsync(WarpModeRestoreMarker marker, string context, CancellationToken ct)
    {
        try
        {
            var current = await QueryModeUnlockedAsync(ct);
            if (current is null)
            {
                _logger.Warning(
                    $"WARP mode could not be read; keeping the restore marker to retry on next start ({context}).",
                    LogTarget.All);
                return;
            }

            var target = WarpModePolicy.ShouldRestore(current, marker);
            if (target is null)
            {
                _logger.Info(
                    $"WARP mode is now {current} (changed outside the app); not restoring {marker.OriginalMode}.",
                    LogTarget.All);
                TryDeleteMarker();
                return;
            }

            if (!WarpModePolicy.IsValidModeName(target))
            {
                _logger.Warning($"WARP restore mode '{target}' is not recognised; leaving {current}.", LogTarget.All);
                TryDeleteMarker();
                return;
            }

            var result = await RunCliUnlockedAsync($"mode {target}", ct);
            if (result.ExitCode != 0)
            {
                LogCliOutputOnce("mode", result);
                _logger.Warning(
                    $"WARP mode restore {current} → {target} failed; keeping the marker to retry on next start.",
                    LogTarget.All);
                return;
            }

            TryDeleteMarker();
            _logger.Info($"WARP mode {current} → {target} restored ({context}).", LogTarget.All);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP mode restore failed ({context}): {ex.Message}", LogTarget.All);
        }
    }

    private async Task<string?> QueryModeUnlockedAsync(CancellationToken ct)
    {
        try
        {
            var result = await RunCliUnlockedAsync("-j settings", ct);
            return WarpModePolicy.ParseOperationMode(result.Output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"WARP mode read failed: {ex.Message}", LogTarget.File);
            return null;
        }
    }

    private WarpModeRestoreMarker? TryReadMarker()
    {
        try
        {
            return WarpModePolicy.DeserializeMarker(File.ReadAllText(MarkerPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void TryWriteMarker(WarpModeRestoreMarker marker)
    {
        try
        {
            File.WriteAllText(MarkerPath, WarpModePolicy.SerializeMarker(marker));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"WARP mode restore marker write failed: {ex.Message}", LogTarget.All);
        }
    }

    private void TryDeleteMarker()
    {
        try
        {
            File.Delete(MarkerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"WARP mode restore marker delete failed: {ex.Message}", LogTarget.File);
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
            if (result.ExitCode != 0)
            {
                LogCliOutputOnce("disconnect", result);
            }
            else
            {
                _logger.Info("WARP disconnect completed.", LogTarget.File | LogTarget.Console);
            }

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

    private Task<WarpCliRunResult> RunCliUnlockedAsync(string arguments, CancellationToken ct)
    {
        return WarpCliProcess.RunAsync(
            ResolvedExecutablePath,
            $"--accept-tos {arguments}",
            WarpCliProcess.DeadlineFor(arguments),
            ct);
    }

    private void LogCliOutputOnce(string operation, WarpCliRunResult result)
    {
        var trimmed = result.Output.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            _logger.Warning(
                $"WARP {operation} failed (exit {result.ExitCode}) with empty CLI output.",
                LogTarget.File | LogTarget.Console);
            return;
        }

        _logger.Warning(
            $"WARP {operation} failed (exit {result.ExitCode}): {trimmed}",
            LogTarget.File | LogTarget.Console);
    }

    private void AddLease(WarpLeaseReason reason, bool tunnelActive)
    {
        lock (_stateLock)
        {
            _leases.Add(reason);
            if (tunnelActive)
            {
                _tunnelLeases.Add(reason);
            }
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

}
