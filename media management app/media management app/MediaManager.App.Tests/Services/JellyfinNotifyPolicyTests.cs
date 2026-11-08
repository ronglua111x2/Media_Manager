using media_management_app.Common;
using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

public class JellyfinNotifyPolicyTests
{
    [Theory]
    // A block WARP may get past: hold the notify and go through the tunnel.
    [InlineData(NetFailure.ConnectRefused, true, false)]
    [InlineData(NetFailure.TlsFailure, true, false)]
    [InlineData(NetFailure.Timeout, true, false)]
    // Recovery switched off: nothing to wait for.
    [InlineData(NetFailure.ConnectRefused, false, true)]
    // Not a block: the library change does not depend on TMDB.
    [InlineData(NetFailure.AuthRejected, true, true)]
    [InlineData(NetFailure.ServerError, true, true)]
    [InlineData(NetFailure.RateLimited, true, true)]
    [InlineData(NetFailure.Offline, true, true)]
    [InlineData(NetFailure.Unknown, true, true)]
    public void NotifyWithoutTunnel(NetFailure failure, bool recoveryAllowed, bool expected)
    {
        Assert.Equal(expected, JellyfinNotifyPolicy.NotifyWithoutTunnel(failure, recoveryAllowed));
    }

    [Theory]
    [InlineData(NetFailure.AuthRejected, true)]
    [InlineData(NetFailure.ServerError, false)]
    [InlineData(NetFailure.ConnectRefused, false)]
    [InlineData(NetFailure.Timeout, false)]
    public void DropPaths_OnlyWhenJellyfinRefusesTheKey(NetFailure failure, bool expected)
    {
        Assert.Equal(expected, JellyfinNotifyPolicy.DropPaths(failure));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void RecoveryBreaker_StopsAfterTwoShows(int exhaustedShows, bool expected)
    {
        Assert.Equal(expected, TmdbWarpRecovery.ShouldStopRecovering(exhaustedShows));
    }
}
