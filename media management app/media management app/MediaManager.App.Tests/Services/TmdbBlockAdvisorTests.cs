using System.Net.Http;
using System.Net.Sockets;
using media_management_app.Services;
using MediaManager.App.Tests.Cleanup;

namespace MediaManager.App.Tests.Services;

public class TmdbBlockAdvisorTests
{
    private static readonly DateTime T0 = new(2026, 10, 31, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Message_DnsOnlyDisconnected_TellsToSwitchMode()
    {
        var message = TmdbBlockAdvisor.BuildMessage("SSL/TLS", warpAvailable: true, warpConnected: false, warpMode: "doh");

        Assert.Contains("WARP: disconnected, mode=doh", message);
        Assert.Contains("DNS-only", message);
        Assert.Contains("warp+doh", message);
    }

    [Fact]
    public void Message_TunnelConnected_SaysStillUnreachable()
    {
        var message = TmdbBlockAdvisor.BuildMessage("SSL/TLS", warpAvailable: true, warpConnected: true, warpMode: "warp+doh");

        Assert.Contains("connected in warp+doh", message);
        Assert.Contains("still unreachable", message);
    }

    [Fact]
    public void Message_NoWarpCli_SaysInstall()
    {
        Assert.Contains("WARP CLI not found", TmdbBlockAdvisor.BuildMessage("ConnectionError", false, false, null));
    }

    [Fact]
    public void TryBeginWarning_RateLimitedToOncePerInterval()
    {
        var advisor = new TmdbBlockAdvisor(new FakeWarpCliService(), new FakeAppLogger(), () => T0);

        Assert.True(advisor.TryBeginWarning(T0));
        Assert.False(advisor.TryBeginWarning(T0.AddMinutes(5)));
        Assert.True(advisor.TryBeginWarning(T0 + TmdbBlockAdvisor.WarnInterval));
    }

    [Fact]
    public void Warn_InsideRecoveryScope_DoesNothing()
    {
        var advisor = new TmdbBlockAdvisor(new FakeWarpCliService(), new FakeAppLogger(), () => T0);

        using (TmdbWarpRecovery.EnterRecoveryScope())
        {
            advisor.Warn(Refused());
        }

        // The scope swallowed the warning, so the rate-limit window is still open.
        Assert.True(advisor.TryBeginWarning(T0));
    }

    [Fact]
    public void Warn_NonNetworkError_DoesNothing()
    {
        var advisor = new TmdbBlockAdvisor(new FakeWarpCliService(), new FakeAppLogger(), () => T0);

        advisor.Warn(new InvalidOperationException("bad token"));

        Assert.True(advisor.TryBeginWarning(T0));
    }

    [Fact]
    public void Warn_NetworkBlock_ConsumesRateLimit()
    {
        var advisor = new TmdbBlockAdvisor(new FakeWarpCliService(), new FakeAppLogger(), () => T0);

        advisor.Warn(Refused());

        Assert.False(advisor.TryBeginWarning(T0.AddMinutes(1)));
    }

    private static HttpRequestException Refused() => new(
        HttpRequestError.ConnectionError,
        "No connection could be made because the target machine actively refused it. (api.themoviedb.org:443)",
        new SocketException((int)SocketError.ConnectionRefused));

    private sealed class FakeWarpCliService : IWarpCliService
    {
        public bool IsAvailable => false;

        public string ResolvedExecutablePath => "warp-cli.exe";

        public bool InFlight => false;

        public bool LastKnownConnected => false;

        public IReadOnlySet<WarpLeaseReason> ActiveLeases => new HashSet<WarpLeaseReason>();

        public bool HasAutoTrackPipelineLease => false;

        public event EventHandler<WarpConnectionChangedEventArgs>? ConnectionChanged
        {
            add { }
            remove { }
        }

        public Task<WarpLeaseResult> AcquireAsync(WarpLeaseReason reason, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult(new WarpLeaseResult(false, false));

        public Task ReleaseAsync(WarpLeaseReason reason, CancellationToken ct = default) => Task.CompletedTask;

        public Task ForceDisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct = default) => Task.FromResult(false);

        public Task<WarpConnectAttempt> ConnectOwnedAsync(TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult(new WarpConnectAttempt(false, false));

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsConnectedAsync(CancellationToken ct = default) => Task.FromResult(false);

        public Task<string?> GetOperationModeAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task RestorePendingModeAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
