using System.Diagnostics;
using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>Runs one warp-cli command (arguments already include <c>--accept-tos</c>). Tests inject a fake.</summary>
public delegate Task<WarpCliRunResult> WarpCliRunner(
    string fileName,
    string arguments,
    TimeSpan deadline,
    CancellationToken cancellationToken);

public sealed class WarpCliService : IWarpCliService, IDisposable
{
    /// <summary>Prefix on every log line that records a WARP mode change or connect/disconnect, for grep.</summary>
    public const string LogPrefix = "[WARP]";

    private const int StatusPollIntervalMilliseconds = 2000;

    private readonly ISettingsService _settingsService;
    private readonly IWindowsNotificationService _windowsNotificationService;
    private readonly IAppLogger _logger;
    private readonly WarpCliRunner _runCli;
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
    private string? _lastKnownMode;
    private bool _disposed;

    public WarpCliService(
        ISettingsService settingsService,
        IWindowsNotificationService windowsNotificationService,
        IAppLifecycleService lifecycleService,
        IAppLogger logger,
        WarpCliRunner? runCli = null)
    {
        _settingsService = settingsService;
        _windowsNotificationService = windowsNotificationService;
        _logger = logger;
        _runCli = runCli ?? WarpCliProcess.RunAsync;
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

    public string? LastKnownMode => Volatile.Read(ref _lastKnownMode);

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
        var label = WarpLeaseReasonText.Label(reason);
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
            var (modeSwitched, tunnelActive, readMode) = await EnsureTunnelModeUnlockedAsync(reason, alreadyConnected, ct);
            if (alreadyConnected && modeSwitched &&
                !await WaitUntilConnectedUnlockedAsync(timeout, ct))
            {
                // The mode change reconnects an existing tunnel; it did not come back.
                _logger.Info(
                    $"{LogPrefix} Reconnect did not finish within {timeout.TotalSeconds:0}s after the mode switch for {label}; connected=no.",
                    LogTarget.All);
                await RestoreModeIfUnneededUnlockedAsync(ct, $"reconnect after mode switch did not finish ({label})");
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
                // The status read above is the truth; keep the cached flag in step so later [WARP] lines do not say connected=no.
                SetLastKnown(true);
                _logger.Debug(
                    $"WARP lease acquired ({label}); tunnel already up.",
                    LogTarget.File | LogTarget.Console);
                RaiseChanged(WarpConnectionChangeSource.Cli);
                return new WarpLeaseResult(true, false, tunnelActive);
            }

            var modeText = DescribeModeAfterSwitch(readMode);
            var connectStarted = Stopwatch.GetTimestamp();
            _logger.Info(
                $"{LogPrefix} Before connect for {label}: mode={modeText}, connected=no (status read just now). Running warp-cli connect; waiting up to {timeout.TotalSeconds:0}s.",
                LogTarget.All);
            var connectResult = await RunCliUnlockedAsync("connect", ct);
            if (connectResult.ExitCode != 0)
            {
                LogCliOutputOnce("connect", connectResult);
            }

            var connected = await WaitUntilConnectedUnlockedAsync(timeout, ct);
            if (!connected)
            {
                _logger.Info(
                    $"{LogPrefix} Connect for {label} timed out after {timeout.TotalSeconds:0}s ({ElapsedText(connectStarted)}, connect exit {connectResult.ExitCode}); connected=no.",
                    LogTarget.All);
                await RestoreModeIfUnneededUnlockedAsync(ct, $"connect timed out ({label})");
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
            _logger.Info(
                $"{LogPrefix} Connected for {label} in {ElapsedText(connectStarted)}: mode={modeText}, connected=yes. This app owns the tunnel and disconnects it when the last lease is released.",
                LogTarget.All);
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
            _logger.Info(
                $"{LogPrefix} Connect for {label} stopped with an error: {ex.Message}. Restoring the user's mode if the app switched it.",
                LogTarget.All);
            await RestoreModeIfUnneededUnlockedAsync(CancellationToken.None, $"connect stopped with an error ({label})");
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
        var label = WarpLeaseReasonText.Label(reason);
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
                    $"WARP lease release ignored ({label}); not held.",
                    LogTarget.File | LogTarget.Console);
                return;
            }

            if (remaining > 0)
            {
                _logger.Debug(
                    $"WARP lease released ({label}); {remaining} lease(s) remain.",
                    LogTarget.File | LogTarget.Console);
                await RestoreModeIfUnneededUnlockedAsync(ct, $"{label} released; {remaining} lease(s) remain");
                RaiseChanged(WarpConnectionChangeSource.Cli);
                return;
            }

            if (!ownedByApp)
            {
                _logger.Debug(
                    $"WARP lease released ({label}); tunnel was already up before acquire — leaving connected.",
                    LogTarget.File | LogTarget.Console);
                await RestoreModeIfUnneededUnlockedAsync(ct, $"{label} released; WARP was already up before acquire");
                RaiseChanged(WarpConnectionChangeSource.Cli);
                return;
            }

            var cause = $"last lease released ({label})";
            await DisconnectCliUnlockedAsync(ct, cause, logAsForce: false);
            await RestoreModeIfUnneededUnlockedAsync(ct, cause);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Info($"{LogPrefix} Lease release failed ({label}): {ex.Message}", LogTarget.All);
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

            var cause = string.IsNullOrEmpty(remaining)
                ? "force-disconnect requested"
                : $"force-disconnect while leases were active: {remaining}";
            if (!string.IsNullOrEmpty(remaining))
            {
                _logger.Info(
                    $"{LogPrefix} Force-disconnect while leases were active: {remaining}.",
                    LogTarget.All);
            }

            await DisconnectCliUnlockedAsync(ct, cause, logAsForce: true);
            await RestoreModeIfUnneededUnlockedAsync(ct, cause);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Info($"{LogPrefix} Force-disconnect failed: {ex.Message}", LogTarget.All);
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
                _logger.Info($"{LogPrefix} Mode restore marker unreadable; deleting {MarkerPath}.", LogTarget.All);
                TryDeleteMarker();
                return;
            }

            _logger.Info(
                $"{LogPrefix} Found mode restore marker: mode was switched to {marker.AppliedMode} by a previous session (since {marker.AppliedUtc:u}); restoring {marker.OriginalMode} (after unclean exit).",
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
            _logger.Info($"{LogPrefix} Mode restore after unclean exit failed: {ex.Message}", LogTarget.All);
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
    /// Mode: the mode the CLI reported during this call, or null when it was not read.
    /// </summary>
    private async Task<(bool Switched, bool TunnelActive, string? Mode)> EnsureTunnelModeUnlockedAsync(
        WarpLeaseReason reason,
        bool connected,
        CancellationToken ct)
    {
        var label = WarpLeaseReasonText.Label(reason);
        if (!WarpModePolicy.LeaseNeedsTunnel(reason, _settingsService.Current.Warp))
        {
            return (false, false, null);
        }

        if (_modeSwitchedFrom is not null)
        {
            return (false, true, null);
        }

        var mode = await QueryModeUnlockedAsync(ct);
        if (mode is null)
        {
            _logger.Info(
                $"{LogPrefix} Mode could not be read; not switching it for {label}.",
                LogTarget.All);
            return (false, false, null);
        }

        if (WarpModePolicy.IsTunnelMode(mode))
        {
            return (false, true, mode);
        }

        if (!WarpModePolicy.IsValidModeName(mode))
        {
            _logger.Info($"{LogPrefix} Mode '{mode}' is not recognised; not switching it.", LogTarget.All);
            return (false, false, mode);
        }

        _logger.Info(
            $"{LogPrefix} Before mode switch for {label}: mode={mode}, connected={YesNo(connected)} (status read just now). Switching to {WarpModePolicy.AppTunnelMode}.",
            LogTarget.All);

        // Marker first: if the app dies before restoring, the next start puts the user's mode back.
        var started = Stopwatch.GetTimestamp();
        TryWriteMarker(new WarpModeRestoreMarker(mode, WarpModePolicy.AppTunnelMode, DateTime.UtcNow));
        WarpCliRunResult result;
        try
        {
            result = await RunCliUnlockedAsync($"mode {WarpModePolicy.AppTunnelMode}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Info(
                $"{LogPrefix} Mode switch {mode} → {WarpModePolicy.AppTunnelMode} for {label} threw after {ElapsedText(started)}: {ex.Message}. Mode state unknown; restore marker kept for the next start.",
                LogTarget.All);
            throw;
        }

        if (result.ExitCode != 0)
        {
            LogCliOutputOnce("mode", result);
            TryDeleteMarker();
            _logger.Info(
                $"{LogPrefix} Mode switch {mode} → {WarpModePolicy.AppTunnelMode} for {label} failed after {ElapsedText(started)} (warp-cli exit {result.ExitCode}): mode unchanged ({mode}), no tunnel for this lease.",
                LogTarget.All);
            return (false, false, mode);
        }

        _modeSwitchedFrom = mode;
        _logger.Info(
            $"{LogPrefix} Mode switched {mode} → {WarpModePolicy.AppTunnelMode} for {label} in {ElapsedText(started)} (warp-cli exit 0). DNS-only modes cannot reach TMDB or most search engines; mode {mode} is restored when the last tunnel lease ends.",
            LogTarget.All);
        return (true, true, mode);
    }

    /// <summary>Restores the user's mode once no held lease needs the tunnel.</summary>
    private async Task RestoreModeIfUnneededUnlockedAsync(CancellationToken ct, string context)
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
                _logger.Debug(
                    $"{LogPrefix} Mode {WarpModePolicy.AppTunnelMode} kept ({context}): a tunnel lease is still held.",
                    LogTarget.File | LogTarget.Console);
                return;
            }
        }

        await RestoreFromMarkerUnlockedAsync(
            new WarpModeRestoreMarker(original, WarpModePolicy.AppTunnelMode, DateTime.UtcNow),
            context,
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
                _logger.Info(
                    $"{LogPrefix} Mode restore skipped ({context}): current mode could not be read; {marker.OriginalMode} not restored. Restore marker kept for the next start.",
                    LogTarget.All);
                return;
            }

            var target = WarpModePolicy.ShouldRestore(current, marker);
            if (target is null)
            {
                _logger.Info(
                    $"{LogPrefix} Mode restore skipped ({context}): current mode is {current}, not {marker.AppliedMode}, so it was changed outside the app; {marker.OriginalMode} not restored. Restore marker deleted.",
                    LogTarget.All);
                TryDeleteMarker();
                return;
            }

            if (!WarpModePolicy.IsValidModeName(target))
            {
                _logger.Info(
                    $"{LogPrefix} Mode restore skipped ({context}): restore target '{target}' is not recognised; mode stays {current}. Restore marker deleted.",
                    LogTarget.All);
                TryDeleteMarker();
                return;
            }

            _logger.Info(
                $"{LogPrefix} Before restore ({context}): mode={current}, connected={YesNo(LastKnownConnected)} (last status read). Restoring {target}.",
                LogTarget.All);
            var started = Stopwatch.GetTimestamp();
            var result = await RunCliUnlockedAsync($"mode {target}", ct);
            if (result.ExitCode != 0)
            {
                LogCliOutputOnce("mode", result);
                _logger.Info(
                    $"{LogPrefix} Mode restore {current} → {target} failed ({context}) after {ElapsedText(started)} (warp-cli exit {result.ExitCode}): mode stays {current}. Restore marker kept; the next start retries it.",
                    LogTarget.All);
                return;
            }

            TryDeleteMarker();
            _logger.Info(
                $"{LogPrefix} Mode restored {current} → {target} ({context}) in {ElapsedText(started)}; restore marker deleted.",
                LogTarget.All);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Info(
                $"{LogPrefix} Mode restore ({context}) failed: {ex.Message}. Mode state unknown; restore marker kept for the next start.",
                LogTarget.All);
        }
    }

    private async Task<string?> QueryModeUnlockedAsync(CancellationToken ct)
    {
        try
        {
            var result = await RunCliUnlockedAsync("-j settings", ct);
            var mode = WarpModePolicy.ParseOperationMode(result.Output);
            if (mode is not null)
            {
                Volatile.Write(ref _lastKnownMode, mode);
            }

            return mode;
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
            _logger.Info($"{LogPrefix} Mode restore marker write failed: {ex.Message}", LogTarget.All);
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
            _logger.Info($"{LogPrefix} Mode restore marker delete failed: {ex.Message}", LogTarget.File);
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

    private async Task DisconnectCliUnlockedAsync(CancellationToken ct, string cause, bool logAsForce)
    {
        if (!IsAvailable)
        {
            _logger.Info(
                $"{LogPrefix} Disconnect skipped ({cause}): warp-cli not available at {ResolvedExecutablePath}; marking connected=no.",
                LogTarget.All);
            SetLastKnown(false);
            RaiseChanged(logAsForce ? WarpConnectionChangeSource.User : WarpConnectionChangeSource.Cli);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            _logger.Info(
                $"{LogPrefix} Before disconnect ({cause}): connected={YesNo(LastKnownConnected)} (last status read), mode={DescribeKnownMode()}. Running warp-cli disconnect.",
                LogTarget.All);
            var result = await RunCliUnlockedAsync("disconnect", ct);
            if (result.ExitCode != 0)
            {
                LogCliOutputOnce("disconnect", result);
                _logger.Info(
                    $"{LogPrefix} Disconnect ({cause}) failed after {ElapsedText(started)} (warp-cli exit {result.ExitCode}); marking connected=no, the real state is not verified.",
                    LogTarget.All);
            }
            else
            {
                _logger.Info(
                    $"{LogPrefix} Disconnected ({cause}) in {ElapsedText(started)} (warp-cli exit 0); connected=no.",
                    LogTarget.All);
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
            _logger.Info(
                $"{LogPrefix} Disconnect ({cause}) failed after {ElapsedText(started)}: {ex.Message}. Connected state not verified.",
                LogTarget.All);
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
        return _runCli(
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
                $"{LogPrefix} warp-cli {operation} failed (exit {result.ExitCode}) with empty CLI output.",
                LogTarget.File | LogTarget.Console);
            return;
        }

        _logger.Warning(
            $"{LogPrefix} warp-cli {operation} failed (exit {result.ExitCode}): {trimmed}",
            LogTarget.File | LogTarget.Console);
    }

    /// <summary>Mode text for connect lines: the app's tunnel mode once switched, else what this call read.</summary>
    private string DescribeModeAfterSwitch(string? readMode)
    {
        if (_modeSwitchedFrom is not null)
        {
            return WarpModePolicy.AppTunnelMode;
        }

        return readMode is null ? DescribeKnownMode() : readMode;
    }

    /// <summary>Mode text for disconnect lines: uses only what the app already holds, never a new CLI call.</summary>
    private string DescribeKnownMode()
    {
        if (_modeSwitchedFrom is not null)
        {
            return $"{WarpModePolicy.AppTunnelMode} (app-switched from {_modeSwitchedFrom})";
        }

        var last = LastKnownMode;
        return string.IsNullOrWhiteSpace(last) ? "not read" : $"{last} (last read)";
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string ElapsedText(long startTimestamp) =>
        $"{Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:0} ms";

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
