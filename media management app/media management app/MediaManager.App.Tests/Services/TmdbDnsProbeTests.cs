using System.Net;
using media_management_app.Common;

namespace MediaManager.App.Tests.Services;

public class TmdbDnsProbeTests
{
    // What the router DNS returned for api.themoviedb.org on 2026-10-25.
    private static readonly IPAddress[] BlockedAnswer = [IPAddress.IPv6Loopback, IPAddress.Loopback];
    private static readonly IPAddress[] RealAnswer = [IPAddress.Parse("13.224.245.47")];

    [Fact]
    public void IsUsable_LoopbackOnly_IsFalse()
    {
        Assert.False(TmdbDnsProbe.IsUsable(BlockedAnswer));
        Assert.False(TmdbDnsProbe.IsUsable([]));
    }

    [Fact]
    public void IsUsable_RoutableAddress_IsTrue()
    {
        Assert.True(TmdbDnsProbe.IsUsable(RealAnswer));
    }

    [Fact]
    public async Task WaitUntilUsable_ReturnsOnceDnsSwitchesAwayFromLoopback()
    {
        var calls = 0;
        Task<IPAddress[]> Resolve(string host, CancellationToken ct)
        {
            Assert.Equal(TmdbDnsProbe.ApiHost, host);
            calls++;
            return Task.FromResult(calls < 3 ? BlockedAnswer : RealAnswer);
        }

        var result = await TmdbDnsProbe.WaitUntilUsableAsync(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None,
            Resolve);

        Assert.True(result.Ready);
        Assert.Equal(3, calls);
        Assert.Equal(RealAnswer, result.Addresses);
    }

    [Fact]
    public async Task WaitUntilUsable_TimesOutWithLastAnswer()
    {
        var result = await TmdbDnsProbe.WaitUntilUsableAsync(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None,
            (_, _) => Task.FromResult(BlockedAnswer));

        Assert.False(result.Ready);
        Assert.Equal(BlockedAnswer, result.Addresses);
    }
}
