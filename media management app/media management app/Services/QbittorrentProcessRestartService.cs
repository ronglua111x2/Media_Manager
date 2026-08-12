using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class QbittorrentProcessRestartService : IQbittorrentProcessRestartService
{
    private const string ProcessName = "qbittorrent";
    private static readonly TimeSpan ProbePollInterval = TimeSpan.FromSeconds(2);

    private readonly ISettingsService _settingsService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IWindowsNotificationService _windowsNotificationService;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private readonly Queue<DateTime> _restartUtcHistory = new();
    private DateTime? _lastRestartAttemptUtc;
    private bool _restartInFlight;

    public QbittorrentProcessRestartService(
        ISettingsService settingsService,
        IQbittorrentClient qbittorrentClient,
        IWindowsNotificationService windowsNotificationService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _qbittorrentClient = qbittorrentClient;
        _windowsNotificationService = windowsNotificationService;
        _logger = logger;
    }

    public Task<QbittorrentWebUiProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
        => _qbittorrentClient.ProbeWebUiAsync(cancellationToken);

    public async Task<(QbittorrentWebUiProbeResult Probe, QbittorrentRestartOutcome? Restart)> EnsureWebUiForHuntAsync(
        CancellationToken cancellationToken = default)
    {
        var probe = await _qbittorrentClient.ProbeWebUiAsync(cancellationToken);
        if (probe.IsOk)
        {
            return (probe, null);
        }

        if (probe.Status is QbittorrentWebUiProbeStatus.AuthFailed or QbittorrentWebUiProbeStatus.InvalidUrl)
        {
            return (probe, QbittorrentRestartOutcome.NotAttempted(
                $"Restart skipped for {probe.Status}: {probe.Detail}"));
        }

        // Unreachable only from here.
        var settings = _settingsService.Current.AutoTorrent.ProcessRestart
                       ?? new QbittorrentProcessRestartSettings();
        if (!settings.Enabled)
        {
            return (probe, new QbittorrentRestartOutcome
            {
                Kind = QbittorrentRestartOutcomeKind.SkippedDisabled,
                Detail = "Process recovery is disabled."
            });
        }

        QbittorrentRestartOutcome outcome;
        lock (_gate)
        {
            if (_restartInFlight)
            {
                return (probe, new QbittorrentRestartOutcome
                {
                    Kind = QbittorrentRestartOutcomeKind.SkippedInFlight,
                    Detail = "A qBittorrent restart is already in progress."
                });
            }

            if (!TryAcquireRestartSlotLocked(settings, out var skipKind, out var skipDetail))
            {
                return (probe, new QbittorrentRestartOutcome
                {
                    Kind = skipKind,
                    Detail = skipDetail
                });
            }

            _restartInFlight = true;
        }

        try
        {
            outcome = await RestartForBindFailAsync(settings, cancellationToken);
        }
        finally
        {
            lock (_gate)
            {
                _restartInFlight = false;
            }
        }

        var afterProbe = await _qbittorrentClient.ProbeWebUiAsync(cancellationToken);
        return (afterProbe, outcome);
    }

    private bool TryAcquireRestartSlotLocked(
        QbittorrentProcessRestartSettings settings,
        out QbittorrentRestartOutcomeKind skipKind,
        out string skipDetail)
    {
        var now = DateTime.UtcNow;
        while (_restartUtcHistory.Count > 0 && now - _restartUtcHistory.Peek() > TimeSpan.FromHours(1))
        {
            _restartUtcHistory.Dequeue();
        }

        if (_lastRestartAttemptUtc is not null &&
            now - _lastRestartAttemptUtc.Value < TimeSpan.FromMinutes(settings.CooldownMinutes))
        {
            skipKind = QbittorrentRestartOutcomeKind.SkippedCooldown;
            skipDetail =
                $"Restart cooldown active ({settings.CooldownMinutes} min since last attempt).";
            return false;
        }

        if (_restartUtcHistory.Count >= settings.MaxRestartsPerHour)
        {
            skipKind = QbittorrentRestartOutcomeKind.SkippedCooldown;
            skipDetail =
                $"Max restarts per hour reached ({settings.MaxRestartsPerHour}/hour).";
            return false;
        }

        skipKind = QbittorrentRestartOutcomeKind.NotAttempted;
        skipDetail = string.Empty;
        return true;
    }

    private async Task<QbittorrentRestartOutcome> RestartForBindFailAsync(
        QbittorrentProcessRestartSettings settings,
        CancellationToken cancellationToken)
    {
        var processes = Process.GetProcessesByName(ProcessName);
        try
        {
            if (processes.Length == 0)
            {
                var skipped = new QbittorrentRestartOutcome
                {
                    Kind = QbittorrentRestartOutcomeKind.SkippedNoProcess,
                    Detail = "qBittorrent process is not running; recovery will not start a new instance."
                };
                Notify("qBittorrent", skipped.Detail!, NotificationKind.QbittorrentRestartFailed);
                return skipped;
            }

            if (!TryGetWebUiPort(out var port, out var portError))
            {
                var failed = new QbittorrentRestartOutcome
                {
                    Kind = QbittorrentRestartOutcomeKind.Failed,
                    Detail = portError,
                    BeforePid = processes[0].Id
                };
                Notify("qBittorrent", failed.Detail!, NotificationKind.QbittorrentRestartFailed);
                return failed;
            }

            if (TryGetListeningPid(port, out var listeningPid) &&
                listeningPid > 0 &&
                !processes.Any(p => p.Id == listeningPid))
            {
                string ownerName;
                try
                {
                    ownerName = Process.GetProcessById(listeningPid).ProcessName;
                }
                catch
                {
                    ownerName = "unknown";
                }

                var conflict = new QbittorrentRestartOutcome
                {
                    Kind = QbittorrentRestartOutcomeKind.PortConflict,
                    Detail =
                        $"WebUI port {port} is owned by PID {listeningPid} ({ownerName}), not qbittorrent. Recovery skipped.",
                    BeforePid = processes[0].Id
                };
                _logger.Warning(conflict.Detail!, LogTarget.All);
                Notify("qBittorrent", conflict.Detail!, NotificationKind.QbittorrentPortConflict);
                return conflict;
            }

            var beforePid = processes[0].Id;
            var exePath = string.IsNullOrWhiteSpace(settings.ExecutablePath)
                ? QbittorrentProcessRestartSettings.DefaultExecutablePath
                : settings.ExecutablePath.Trim();

            if (!File.Exists(exePath))
            {
                var missing = new QbittorrentRestartOutcome
                {
                    Kind = QbittorrentRestartOutcomeKind.Failed,
                    Detail = $"qBittorrent executable not found at '{exePath}'.",
                    BeforePid = beforePid
                };
                Notify("qBittorrent", missing.Detail!, NotificationKind.QbittorrentRestartFailed);
                return missing;
            }

            RecordRestartAttempt();
            _logger.Warning(
                $"qBittorrent WebUI bind-fail recovery: before PID={beforePid}, graceful={settings.GracefulShutdownSeconds}s, exe='{exePath}'.",
                LogTarget.All);

            await StopProcessesGracefullyAsync(processes, settings.GracefulShutdownSeconds, cancellationToken);

            var started = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty
            });

            var afterPid = started?.Id;
            _logger.Info(
                $"qBittorrent started after recovery: after PID={afterPid?.ToString() ?? "unknown"}. Waiting for WebUI up to {settings.WebUiReadyTimeoutSeconds}s.",
                LogTarget.All);

            var ready = await WaitForWebUiAsync(
                TimeSpan.FromSeconds(settings.WebUiReadyTimeoutSeconds),
                cancellationToken);

            // Bound (Ok or AuthFailed) means restart brought the WebUI listener back.
            if (ready.IsOk || ready.Status == QbittorrentWebUiProbeStatus.AuthFailed)
            {
                var ok = new QbittorrentRestartOutcome
                {
                    Kind = QbittorrentRestartOutcomeKind.Succeeded,
                    Detail = ready.IsOk
                        ? $"qBittorrent restarted (before PID {beforePid} → after PID {afterPid}). WebUI OK."
                        : $"qBittorrent restarted (before PID {beforePid} → after PID {afterPid}). WebUI bound but auth failed.",
                    BeforePid = beforePid,
                    AfterPid = afterPid,
                    WebUiOk = ready.IsOk
                };
                _logger.Info(ok.Detail!, LogTarget.All);
                Notify("qBittorrent", ok.Detail!, NotificationKind.QbittorrentRestarted);
                return ok;
            }

            var stillDown = new QbittorrentRestartOutcome
            {
                Kind = QbittorrentRestartOutcomeKind.Failed,
                Detail =
                    $"qBittorrent restarted (before PID {beforePid} → after PID {afterPid}) but WebUI still down: {ready.Detail}",
                BeforePid = beforePid,
                AfterPid = afterPid,
                WebUiOk = false
            };
            _logger.Warning(stillDown.Detail!, LogTarget.All);
            Notify("qBittorrent", stillDown.Detail!, NotificationKind.QbittorrentRestartFailed);
            return stillDown;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private void RecordRestartAttempt()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            _lastRestartAttemptUtc = now;
            _restartUtcHistory.Enqueue(now);
        }
    }

    private async Task<QbittorrentWebUiProbeResult> WaitForWebUiAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        QbittorrentWebUiProbeResult last = QbittorrentWebUiProbeResult.Unreachable("WebUI not ready yet.");
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = await _qbittorrentClient.ProbeWebUiAsync(cancellationToken);
            if (last.IsOk)
            {
                return last;
            }

            // Auth after restart still means WebUI is bound — treat as ready for hunt gating.
            if (last.Status is QbittorrentWebUiProbeStatus.AuthFailed)
            {
                return last;
            }

            await Task.Delay(ProbePollInterval, cancellationToken);
        }

        return last;
    }

    private static async Task StopProcessesGracefullyAsync(
        Process[] processes,
        int gracefulSeconds,
        CancellationToken cancellationToken)
    {
        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                }
            }
            catch (Exception)
            {
                // Ignore — may already be exiting or have no main window.
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, gracefulSeconds));
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (processes.All(p =>
                {
                    try
                    {
                        return p.HasExited;
                    }
                    catch
                    {
                        return true;
                    }
                }))
            {
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Best-effort kill.
            }
        }
    }

    private bool TryGetWebUiPort(out int port, out string error)
    {
        port = 0;
        error = string.Empty;
        var configuredUrl = _settingsService.Current.AutoTorrent.QbittorrentWebUiUrl;
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri))
        {
            error = "qBittorrent Web UI URL is invalid; cannot resolve port for recovery.";
            return false;
        }

        port = uri.Port;
        if (port <= 0)
        {
            error = "qBittorrent Web UI URL has no valid port.";
            return false;
        }

        return true;
    }

    private void Notify(string title, string message, NotificationKind kind)
    {
        _windowsNotificationService.TryShow(new WindowsNotificationRequest
        {
            Title = title,
            Message = message,
            Kind = kind
        });
    }

    private static bool TryGetListeningPid(int port, out int pid)
    {
        pid = 0;
        try
        {
            foreach (var row in TcpTableHelper.GetListeners())
            {
                if (row.LocalPort == port && row.State == TcpState.Listen)
                {
                    pid = row.OwningPid;
                    return pid > 0;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static class TcpTableHelper
    {
        private const int AfInet = 2;
        private const int TcpTableOwnerPidListener = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcpRowOwnerPid
        {
            public uint State;
            public uint LocalAddr;
            public uint LocalPort;
            public uint RemoteAddr;
            public uint RemotePort;
            public int OwningPid;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int dwOutBufLen,
            bool sort,
            int ipVersion,
            int tableClass,
            uint reserved);

        public readonly record struct ListenerRow(int LocalPort, TcpState State, int OwningPid);

        public static IEnumerable<ListenerRow> GetListeners()
        {
            var bufferSize = 0;
            var result = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet, TcpTableOwnerPidListener, 0);
            if (result != 0 && result != 122) // ERROR_INSUFFICIENT_BUFFER
            {
                yield break;
            }

            var buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                result = GetExtendedTcpTable(buffer, ref bufferSize, true, AfInet, TcpTableOwnerPidListener, 0);
                if (result != 0)
                {
                    yield break;
                }

                var rowCount = Marshal.ReadInt32(buffer);
                var rowPtr = IntPtr.Add(buffer, 4);
                var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
                for (var i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(IntPtr.Add(rowPtr, i * rowSize));
                    var localPort = (int)(((row.LocalPort & 0xFF) << 8) | ((row.LocalPort >> 8) & 0xFF));
                    yield return new ListenerRow(localPort, (TcpState)row.State, row.OwningPid);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
