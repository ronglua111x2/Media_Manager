using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using media_management_app.Services;
using MediaManager.App.Tests.Cleanup;

namespace MediaManager.App.Tests.Services;

public class TmdbWarpRecoveryTests
{
    private static readonly IPAddress[] RealAnswer = [IPAddress.Parse("13.224.245.47")];

    private static HttpRequestException Refused() => new(
        HttpRequestError.ConnectionError,
        "No connection could be made because the target machine actively refused it. (api.themoviedb.org:443)",
        new SocketException((int)SocketError.ConnectionRefused));

    private static Task<(bool Ready, IReadOnlyCollection<IPAddress> Addresses)> DnsReady(CancellationToken _) =>
        Task.FromResult<(bool, IReadOnlyCollection<IPAddress>)>((true, RealAnswer));

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public async Task WarpConnects_RetrySucceedsAfterTunnelSettles()
    {
        var calls = 0;
        var delays = new List<TimeSpan>();

        var result = await TmdbWarpRecovery.RetryWithWarpAsync(
            "test",
            Refused(),
            _ =>
            {
                calls++;
                return calls < 3 ? throw Refused() : Task.FromResult("ok");
            },
            _ => Task.FromResult(new WarpLeaseResult(true, true)),
            new FakeAppLogger(),
            CancellationToken.None,
            DnsReady,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.Equal("ok", result);
        Assert.Equal(3, calls);
        Assert.Equal(TmdbWarpRecovery.RetryDelays, delays);
    }

    [Fact]
    public async Task WarpConnectFails_ThrowsConnectFailedWithOriginalError()
    {
        var original = Refused();
        var called = false;

        var ex = await Assert.ThrowsAsync<TmdbWarpConnectFailedException>(() =>
            TmdbWarpRecovery.RetryWithWarpAsync(
                "test",
                original,
                _ =>
                {
                    called = true;
                    return Task.FromResult(true);
                },
                _ => Task.FromResult(new WarpLeaseResult(false, false)),
                new FakeAppLogger(),
                CancellationToken.None,
                DnsReady,
                NoDelay));

        Assert.Same(original, ex.InnerException);
        Assert.False(called);
    }

    [Fact]
    public async Task StillBlockedAfterAllRetries_ThrowsLastError()
    {
        var calls = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            TmdbWarpRecovery.RetryWithWarpAsync<bool>(
                "test",
                Refused(),
                _ =>
                {
                    calls++;
                    throw Refused();
                },
                _ => Task.FromResult(new WarpLeaseResult(true, false)),
                new FakeAppLogger(),
                CancellationToken.None,
                DnsReady,
                NoDelay));

        Assert.Equal(TmdbWarpRecovery.RetryDelays.Length + 1, calls);
    }

    [Fact]
    public async Task NonNetworkError_IsNotRetried()
    {
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TmdbWarpRecovery.RetryWithWarpAsync<bool>(
                "test",
                Refused(),
                _ =>
                {
                    calls++;
                    throw new InvalidOperationException("bad token");
                },
                _ => Task.FromResult(new WarpLeaseResult(true, false)),
                new FakeAppLogger(),
                CancellationToken.None,
                DnsReady,
                NoDelay));

        Assert.Equal(1, calls);
    }
}
