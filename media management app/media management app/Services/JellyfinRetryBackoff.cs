using media_management_app.Common;

namespace media_management_app.Services;

/// <summary>What a failed TMDB pre-probe or Jellyfin notify means for the queued paths.</summary>
public static class JellyfinNotifyPolicy
{
    /// <summary>
    /// Library changes do not depend on TMDB, so only a block WARP can get past delays the notify:
    /// auth errors, outages and a disabled recovery all notify straight away.
    /// </summary>
    public static bool NotifyWithoutTunnel(NetFailure probeFailure, bool recoveryAllowed) =>
        !(recoveryAllowed && NetworkFailureClassifier.IsWarpFallbackCandidate(probeFailure));

    /// <summary>Jellyfin refused the API key: retrying cannot help.</summary>
    public static bool DropPaths(NetFailure notifyFailure) => notifyFailure == NetFailure.AuthRejected;
}

/// <summary>
/// Delay before re-flushing Jellyfin paths after a failed flush. Grows 3 s → 30 s → 2 min → 5 min so a
/// blocked TMDB pre-probe or an offline server does not loop every few seconds.
/// </summary>
public static class JellyfinRetryBackoff
{
    public static readonly TimeSpan Initial = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan[] Steps =
    [
        Initial,
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        Max
    ];

    /// <param name="consecutiveFailures">Failed flushes in a row, counting the one that just failed (≥ 1).</param>
    /// <param name="warpConnectFailed">WARP could not connect for the TMDB pre-probe; wait the longest step.</param>
    public static TimeSpan Next(int consecutiveFailures, bool warpConnectFailed)
    {
        if (warpConnectFailed)
        {
            return Max;
        }

        var index = Math.Clamp(consecutiveFailures - 1, 0, Steps.Length - 1);
        return Steps[index];
    }
}
