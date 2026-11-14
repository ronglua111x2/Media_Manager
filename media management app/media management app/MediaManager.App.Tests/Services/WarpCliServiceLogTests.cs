using System.IO;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.App.Tests.Cleanup;

namespace MediaManager.App.Tests.Services;

/// <summary>
/// Checks the [WARP] log lines written around every WARP mode change and connect/disconnect, using a fake
/// warp-cli runner (no real warp-cli is started) and the capturing FakeAppLogger.
/// </summary>
public sealed class WarpCliServiceLogTests : IDisposable
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(1);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "warp-cli-log-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeSettingsService _settings = new();
    private readonly FakeAppLogger _logger = new();
    private readonly FakeWarpCli _cli = new();

    public WarpCliServiceLogTests()
    {
        Directory.CreateDirectory(_root);
        var executable = Path.Combine(_root, "warp-cli.exe");
        File.WriteAllText(executable, string.Empty);
        _settings.Current.StateFolder = _root;
        _settings.Current.Warp.ExecutablePath = executable;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task TunnelLease_SwitchesAndRestoresMode_LogsEachChangeInOrder()
    {
        _cli.Mode = "doh";
        using var service = CreateService();

        var acquired = await service.AcquireAsync(WarpLeaseReason.TmdbSslRecover, ShortTimeout);
        await service.ReleaseAsync(WarpLeaseReason.TmdbSslRecover);

        Assert.True(acquired.Connected);
        Assert.Equal("doh", _cli.Mode);
        Assert.False(_cli.Connected);
        AssertInOrder(
            WarpInfos(),
            "[WARP] Before mode switch for Auto-Track TMDB recover: mode=doh, connected=no",
            "[WARP] Mode switched doh → warp+doh for Auto-Track TMDB recover in",
            "[WARP] Before connect for Auto-Track TMDB recover: mode=warp+doh, connected=no",
            "[WARP] Connected for Auto-Track TMDB recover in",
            "mode=warp+doh, connected=yes",
            "[WARP] Before disconnect (last lease released (Auto-Track TMDB recover)): connected=yes",
            "[WARP] Disconnected (last lease released (Auto-Track TMDB recover)) in",
            "[WARP] Before restore (last lease released (Auto-Track TMDB recover)): mode=warp+doh",
            "[WARP] Mode restored warp+doh → doh (last lease released (Auto-Track TMDB recover)) in");
        Assert.False(File.Exists(Path.Combine(_root, WarpModePolicy.MarkerFileName)));
    }

    [Fact]
    public async Task ConnectTimeout_LogsBeforeConnectCliFailureAndTimeout()
    {
        _cli.Mode = "doh";
        _cli.ConnectSucceeds = false;
        using var service = CreateService();

        var result = await service.AcquireAsync(WarpLeaseReason.User, ShortTimeout);

        Assert.False(result.Connected);
        // A user lease keeps the user's mode, so the mode was not read for this connect.
        AssertInOrder(
            WarpInfos(),
            "[WARP] Before connect for you: mode=not read, connected=no",
            "[WARP] Connect for you timed out after 1s");
        Assert.Contains(
            _logger.Warnings,
            line => line.StartsWith("[WARP] warp-cli connect failed (exit 1): Registration missing", StringComparison.Ordinal));
        Assert.DoesNotContain(WarpInfos(), line => line.Contains("Mode switched", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreFails_LogsTheFailureAndKeepsTheMarker()
    {
        _cli.Mode = "doh";
        using var service = CreateService();
        await service.AcquireAsync(WarpLeaseReason.TmdbSslRecover, ShortTimeout);
        _cli.FailingModeTarget = "doh";

        await service.ReleaseAsync(WarpLeaseReason.TmdbSslRecover);

        AssertInOrder(
            WarpInfos(),
            "[WARP] Before restore (last lease released (Auto-Track TMDB recover)): mode=warp+doh");
        Assert.Contains(
            WarpInfos(),
            line => line.StartsWith(
                "[WARP] Mode restore warp+doh → doh failed (last lease released (Auto-Track TMDB recover)) after",
                StringComparison.Ordinal) &&
                line.Contains("mode stays warp+doh", StringComparison.Ordinal) &&
                line.Contains("Restore marker kept", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(_root, WarpModePolicy.MarkerFileName)));
        Assert.Equal("warp+doh", _cli.Mode);
    }

    [Fact]
    public async Task ModeChangedOutsideTheApp_LogsRestoreSkipped()
    {
        _cli.Mode = "doh";
        using var service = CreateService();
        await service.AcquireAsync(WarpLeaseReason.TmdbSslRecover, ShortTimeout);
        _cli.Mode = "warp";

        await service.ReleaseAsync(WarpLeaseReason.TmdbSslRecover);

        Assert.Equal("warp", _cli.Mode);
        Assert.Contains(
            WarpInfos(),
            line => line.StartsWith(
                "[WARP] Mode restore skipped (last lease released (Auto-Track TMDB recover)): current mode is warp, not warp+doh, so it was changed outside the app; doh not restored",
                StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_root, WarpModePolicy.MarkerFileName)));
    }

    [Fact]
    public async Task ForceDisconnect_LogsStateAndCauseBeforeAndAfter()
    {
        _cli.Mode = "doh";
        _cli.Connected = true;
        using var service = CreateService();
        await service.AcquireAsync(WarpLeaseReason.User, ShortTimeout);

        await service.ForceDisconnectAsync();

        Assert.False(_cli.Connected);
        AssertInOrder(
            WarpInfos(),
            "[WARP] Before disconnect (force-disconnect while leases were active: you): connected=yes (last status read), mode=not read.",
            "[WARP] Disconnected (force-disconnect while leases were active: you) in");
    }

    private WarpCliService CreateService() =>
        new(_settings, new FakeNotifications(), new FakeLifecycle(), _logger, _cli.RunAsync);

    private List<string> WarpInfos() =>
        _logger.Infos.Where(line => line.StartsWith(WarpCliService.LogPrefix, StringComparison.Ordinal)).ToList();

    /// <summary>Each fragment must appear in a line after the previous fragment's line (the same line may match twice).</summary>
    private static void AssertInOrder(IReadOnlyList<string> lines, params string[] fragments)
    {
        var previous = 0;
        foreach (var fragment in fragments)
        {
            var found = -1;
            for (var i = previous; i < lines.Count; i++)
            {
                if (lines[i].Contains(fragment, StringComparison.Ordinal))
                {
                    found = i;
                    break;
                }
            }

            Assert.True(
                found >= 0,
                $"Missing or out of order: '{fragment}'.{Environment.NewLine}Logged [WARP] lines:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
            previous = found;
        }
    }

    private sealed class FakeNotifications : IWindowsNotificationService
    {
        public void Initialize()
        {
        }

        public bool TryShow(WindowsNotificationRequest request) => true;

        public bool TryShow(string title, string message, string? tag = null) => true;
    }

    private sealed class FakeLifecycle : IAppLifecycleService
    {
        public AppMode CurrentMode => AppMode.Foreground;

        public event EventHandler<AppMode>? AppModeChanged
        {
            add
            {
            }

            remove
            {
            }
        }

        public void EnterBackgroundMode()
        {
        }

        public void EnterForegroundMode()
        {
        }
    }

    /// <summary>Answers the warp-cli commands the service uses from a little in-memory state.</summary>
    private sealed class FakeWarpCli
    {
        private const string AcceptTosPrefix = "--accept-tos ";

        public string Mode { get; set; } = "doh";

        public bool Connected { get; set; }

        public bool ConnectSucceeds { get; set; } = true;

        /// <summary>Mode name whose <c>mode</c> command exits 1; null means every mode change succeeds.</summary>
        public string? FailingModeTarget { get; set; }

        public Task<WarpCliRunResult> RunAsync(
            string fileName,
            string arguments,
            TimeSpan deadline,
            CancellationToken cancellationToken)
        {
            var command = arguments.StartsWith(AcceptTosPrefix, StringComparison.Ordinal)
                ? arguments[AcceptTosPrefix.Length..]
                : arguments;

            WarpCliRunResult result;
            if (command == "-j settings")
            {
                result = new WarpCliRunResult(0, $"{{\"settings\":{{\"operation_mode\":\"{Mode}\"}}}}");
            }
            else if (command == "status")
            {
                result = new WarpCliRunResult(0, Connected ? "Status update: Connected" : "Status update: Disconnected");
            }
            else if (command == "connect")
            {
                if (ConnectSucceeds)
                {
                    Connected = true;
                    result = new WarpCliRunResult(0, "Success");
                }
                else
                {
                    result = new WarpCliRunResult(1, "Registration missing");
                }
            }
            else if (command == "disconnect")
            {
                Connected = false;
                result = new WarpCliRunResult(0, "Success");
            }
            else if (command.StartsWith("mode ", StringComparison.Ordinal))
            {
                var target = command["mode ".Length..];
                if (target == FailingModeTarget)
                {
                    result = new WarpCliRunResult(1, "mode change rejected");
                }
                else
                {
                    Mode = target;
                    result = new WarpCliRunResult(0, "Success");
                }
            }
            else
            {
                result = new WarpCliRunResult(1, $"unexpected command: {command}");
            }

            return Task.FromResult(result);
        }
    }
}
