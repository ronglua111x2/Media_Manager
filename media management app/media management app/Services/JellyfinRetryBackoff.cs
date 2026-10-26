namespace media_management_app.Services;

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
