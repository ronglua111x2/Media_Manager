using media_management_app.Common;

namespace media_management_app.Services;

public interface ITmdbBlockAdvisor
{
    /// <summary>
    /// Logs a console warning (rate-limited) when a TMDB call failed with a network-block error and no WARP
    /// recovery handles it, telling the user to check the WARP mode.
    /// </summary>
    void Warn(Exception exception, CancellationToken cancellationToken = default);
}

/// <summary>
/// TMDB is blocked on this network (DNS answers loopback; TLS cut by SNI). Library, Find/Add and other
/// non-Auto-Track TMDB calls have no WARP recovery, so their failures get a hint about the WARP mode instead.
/// </summary>
public sealed class TmdbBlockAdvisor : ITmdbBlockAdvisor
{
    public static readonly TimeSpan WarnInterval = TimeSpan.FromMinutes(10);

    private readonly IWarpCliService _warpCliService;
    private readonly IAppLogger _logger;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private DateTime? _lastWarnUtc;

    public TmdbBlockAdvisor(IWarpCliService warpCliService, IAppLogger logger)
        : this(warpCliService, logger, () => DateTime.UtcNow)
    {
    }

    public TmdbBlockAdvisor(IWarpCliService warpCliService, IAppLogger logger, Func<DateTime> utcNow)
    {
        _warpCliService = warpCliService;
        _logger = logger;
        _utcNow = utcNow;
    }

    public void Warn(Exception exception, CancellationToken cancellationToken = default)
    {
        if (TmdbWarpRecovery.IsInRecoveryScope)
        {
            return;
        }

        var reason = TmdbNetworkBlockDetector.Describe(exception, cancellationToken);
        if (reason is null || !TryBeginWarning(_utcNow()))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            string? mode = null;
            var warpAvailable = _warpCliService.IsAvailable;
            if (warpAvailable)
            {
                try
                {
                    mode = await _warpCliService.GetOperationModeAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.Debug($"WARP mode read for TMDB hint failed: {ex.Message}", LogTarget.File);
                }
            }

            _logger.Warning(
                BuildMessage(reason, warpAvailable, _warpCliService.LastKnownConnected, mode),
                LogTarget.All);
        });
    }

    /// <summary>At most one warning per <see cref="WarnInterval"/>.</summary>
    public bool TryBeginWarning(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (_lastWarnUtc is { } last && nowUtc - last < WarnInterval)
            {
                return false;
            }

            _lastWarnUtc = nowUtc;
            return true;
        }
    }

    public static string BuildMessage(string reason, bool warpAvailable, bool warpConnected, string? warpMode)
    {
        const string Prefix = "TMDB looks blocked on this network";
        if (!warpAvailable)
        {
            return $"{Prefix} ({reason}). WARP CLI not found — install Cloudflare WARP and use \"Traffic and DNS\" mode (warp+doh) to reach TMDB.";
        }

        var modeText = warpMode ?? "unknown";
        if (warpConnected && WarpModePolicy.IsTunnelMode(warpMode))
        {
            return $"{Prefix} ({reason}). WARP is connected in {modeText} but TMDB is still unreachable — check WARP / the network.";
        }

        var state = warpConnected ? "connected" : "disconnected";
        var modeHint = WarpModePolicy.IsTunnelMode(warpMode)
            ? "connect WARP"
            : $"WARP mode {modeText} is DNS-only and cannot get past the block — connect WARP in \"Traffic and DNS\" mode (warp+doh)";
        return $"{Prefix} ({reason}). WARP: {state}, mode={modeText}. Check the WARP mode: {modeHint}. Only Auto-Track switches the WARP mode automatically.";
    }
}
