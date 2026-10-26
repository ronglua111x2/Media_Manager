using System.Net;
using media_management_app.Common;

namespace media_management_app.Services;

/// <summary>Thrown when WARP could not be connected for a TMDB recover; the inner exception is the original TMDB error.</summary>
public sealed class TmdbWarpConnectFailedException(Exception original)
    : Exception($"{original.Message} (WARP connect failed or timed out)", original);

/// <summary>
/// Shared Auto-Track TMDB recovery after a network-block error: connect WARP, wait until
/// api.themoviedb.org stops resolving to loopback, then retry with spaced attempts.
/// Used by TMDB discovery and the Jellyfin refresh pre-probe.
/// </summary>
public static class TmdbWarpRecovery
{
    public static readonly TimeSpan DnsReadyTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5)];

    /// <summary>
    /// Connects WARP via <paramref name="acquireWarp"/> and retries <paramref name="action"/>.
    /// Throws <see cref="TmdbWarpConnectFailedException"/> when WARP does not connect, or the last
    /// TMDB error when every retry still fails.
    /// </summary>
    public static async Task<T> RetryWithWarpAsync<T>(
        string label,
        Exception originalError,
        Func<CancellationToken, Task<T>> action,
        Func<CancellationToken, Task<WarpLeaseResult>> acquireWarp,
        IAppLogger logger,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<(bool Ready, IReadOnlyCollection<IPAddress> Addresses)>>? waitForDns = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        waitForDns ??= ct => TmdbDnsProbe.WaitUntilUsableAsync(DnsReadyTimeout, TimeSpan.FromSeconds(1), ct);
        delay ??= Task.Delay;

        logger.Warning(
            $"TMDB network error ({TmdbNetworkBlockDetector.Describe(originalError, cancellationToken)}) for {label}: {originalError.Message}. Attempting WARP self-recover.",
            LogTarget.All);

        var attempt = await acquireWarp(cancellationToken);
        if (!attempt.Connected)
        {
            logger.Warning($"WARP connect for TMDB self-recover ({label}) failed or timed out.", LogTarget.All);
            throw new TmdbWarpConnectFailedException(originalError);
        }

        if (attempt.StartedByThisAcquire)
        {
            logger.Info($"WARP connected for TMDB recover ({label}).", LogTarget.File | LogTarget.Console);
        }

        // Fresh WARP: the OS resolver can still hand out the loopback answer for a few seconds.
        var dns = await waitForDns(cancellationToken);
        if (dns.Ready)
        {
            logger.Info(
                $"TMDB DNS ready with WARP: {TmdbDnsProbe.ApiHost} -> {TmdbDnsProbe.Format(dns.Addresses)}.",
                LogTarget.File | LogTarget.Console);
        }
        else
        {
            logger.Warning(
                $"TMDB DNS still not usable after {DnsReadyTimeout.TotalSeconds:0}s with WARP: {TmdbDnsProbe.ApiHost} -> {TmdbDnsProbe.Format(dns.Addresses)}. Retrying anyway.",
                LogTarget.All);
        }

        for (var retry = 1; ; retry++)
        {
            logger.Info(
                $"Retrying TMDB for {label} with WARP (attempt {retry}/{RetryDelays.Length + 1}).",
                LogTarget.All);
            try
            {
                return await action(cancellationToken);
            }
            catch (Exception retryEx) when (retry <= RetryDelays.Length &&
                                            TmdbNetworkBlockDetector.IsLikelyBlocked(retryEx, cancellationToken))
            {
                var wait = RetryDelays[retry - 1];
                logger.Warning(
                    $"TMDB still unreachable with WARP ({TmdbNetworkBlockDetector.Describe(retryEx, cancellationToken)}): {retryEx.Message}. Waiting {wait.TotalSeconds:0}s for the tunnel to settle.",
                    LogTarget.File | LogTarget.Console);
                await delay(wait, cancellationToken);
            }
        }
    }
}
