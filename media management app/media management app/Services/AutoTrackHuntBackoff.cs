using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// When Auto-Track may hunt an episode again after a hunt found nothing (audit 07d GOV-04). Without it a "stuck"
/// episode (aired, nobody uploaded yet, or the recipe is too strict) was searched every 30 minutes all day: up to
/// ~1 400 requests a day per site for 15 such episodes, and the first shows in alphabetical order kept the hunt
/// slots so later shows were never hunted. The wait grows with the episode's age: simulcasts usually appear within
/// hours, old episodes rarely appear suddenly. Uses the order's last update as the last attempt (no migration).
/// </summary>
public static class AutoTrackHuntBackoff
{
    public static TimeSpan RetryInterval(DateTime? airDateLocal, DateTime nowLocal)
    {
        if (airDateLocal is not { } aired)
        {
            return TimeSpan.FromHours(6);
        }

        var age = nowLocal - aired;
        return age switch
        {
            _ when age < TimeSpan.FromHours(12) => TimeSpan.FromHours(1),
            _ when age < TimeSpan.FromDays(2) => TimeSpan.FromHours(3),
            _ when age < TimeSpan.FromDays(7) => TimeSpan.FromHours(6),
            _ => TimeSpan.FromHours(24)
        };
    }

    /// <summary>
    /// The time the episode may be hunted again, or null when it may be hunted now: only an Auto-Track order that
    /// ended in <see cref="TorrentOrderStatus.NoCandidates"/> makes it wait.
    /// </summary>
    public static DateTime? NextAttemptUtc(TorrentCartOrder? order, DateTime? airDateLocal, DateTime nowUtc)
    {
        if (order is not { Source: TorrentOrderSource.AutoTrack, Status: TorrentOrderStatus.NoCandidates })
        {
            return null;
        }

        var next = order.UpdatedUtc + RetryInterval(airDateLocal, nowUtc.ToLocalTime());
        return next > nowUtc ? next : null;
    }
}
