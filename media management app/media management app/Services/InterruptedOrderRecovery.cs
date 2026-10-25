using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public enum InterruptedOrderAction
{
    /// <summary>Order was not interrupted mid-hunt; leave it alone.</summary>
    None,

    /// <summary>Wipe candidates and return to Draft so the next hunt searches again.</summary>
    ResetToDraft,

    /// <summary>Keep candidates and return to CandidatesFound so the next hunt resumes accept/add.</summary>
    ReturnToCandidates,

    /// <summary>qBittorrent already has the torrent: re-run add-gate validation on it, then attach.</summary>
    RevalidateExisting
}

/// <summary>
/// Pure rules for Auto-Track episode orders left in Searching/Approved by a previous app session
/// (graceful shutdown, crash, kill, power loss). Only DB state survives a hard stop, so recovery is
/// decided from the stored order alone plus whether qBittorrent still has its torrent.
/// </summary>
public static class InterruptedOrderRecovery
{
    /// <summary>
    /// True when <paramref name="order"/> is an Auto-Track episode order stuck mid-hunt by a session that
    /// ended before <paramref name="sessionStartUtc"/>. Orders touched in this session are never matched.
    /// </summary>
    public static bool IsInterrupted(TorrentCartOrder order, DateTime sessionStartUtc)
    {
        return order.Source == TorrentOrderSource.AutoTrack &&
               order.TargetKind == MediaKind.TvEpisode &&
               order.EpisodeId is not null &&
               order.Status is TorrentOrderStatus.Searching or TorrentOrderStatus.Approved &&
               string.IsNullOrWhiteSpace(order.TorrentHash) &&
               order.UpdatedUtc.ToUniversalTime() < sessionStartUtc.ToUniversalTime();
    }

    /// <param name="liveTorrentFound">qBittorrent has the pending-validation hash or the selected magnet's infohash.</param>
    /// <param name="hasCandidates">The order still has stored cart candidates.</param>
    public static InterruptedOrderAction Decide(
        TorrentCartOrder order,
        bool liveTorrentFound,
        bool hasCandidates)
    {
        return order.Status switch
        {
            TorrentOrderStatus.Searching => InterruptedOrderAction.ResetToDraft,
            TorrentOrderStatus.Approved when liveTorrentFound && order.HasSelectedCandidate =>
                InterruptedOrderAction.RevalidateExisting,
            TorrentOrderStatus.Approved when order.HasSelectedCandidate && hasCandidates =>
                InterruptedOrderAction.ReturnToCandidates,
            TorrentOrderStatus.Approved => InterruptedOrderAction.ResetToDraft,
            _ => InterruptedOrderAction.None
        };
    }

    /// <summary>
    /// Hashes the add gate put into qBittorrent but has not validated yet. Reconcile seeds its used-hash
    /// set with these so an unvalidated download is never adopted by name.
    /// </summary>
    public static HashSet<string> CollectPendingValidationHashes(IEnumerable<TorrentCartOrder> orders)
    {
        return orders
            .Select(order => order.PendingValidationHash)
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
