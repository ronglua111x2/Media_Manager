namespace media_management_app.Services;

/// <summary>
/// The scheduled torrent hunt runs at most once per hunt interval (audit 07d GOV-04). A hunt that stops in its
/// preflight (WARP or its tunnel unavailable, qBittorrent down) searched nothing, so it hands its slot back and
/// the next 30-minute Auto-Track cycle tries again instead of waiting a whole hunt interval. Guarded by the
/// hunt lock, like the rest of the hunt state.
/// </summary>
public sealed class AutoTrackHuntSlot
{
    private static readonly TimeSpan Slack = TimeSpan.FromMinutes(1);

    private DateTime? _lastUtc;
    private DateTime? _beforeClaimUtc;

    /// <summary>Claims the slot and returns null, or returns when the next scheduled hunt is due.</summary>
    public DateTime? TryClaim(DateTime nowUtc, TimeSpan interval)
    {
        if (_lastUtc is { } last && nowUtc - last < interval - Slack)
        {
            return last + interval;
        }

        _beforeClaimUtc = _lastUtc;
        _lastUtc = nowUtc;
        return null;
    }

    /// <summary>The claimed hunt did not search anything: the slot goes back to the previous hunt's time.</summary>
    public void GiveBack() => _lastUtc = _beforeClaimUtc;
}
