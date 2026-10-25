using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class AutoTrackTmdbEligibility
{
    public static bool ShouldRefreshTmdb(
        TrackedShow show,
        AutoTrackSettings settings,
        IReadOnlyList<TrackedEpisode> episodes,
        DateTime nowLocal,
        out string? skipReason,
        bool bypassAnchor = false)
    {
        skipReason = null;

        if (!show.IsAutoTracked)
        {
            skipReason = "Show is not auto-tracked";
            return false;
        }

        if (!bypassAnchor && HasSatisfiedCurrentCycle(show, settings, nowLocal))
        {
            var cycleAnchor = AutoTrackWeekAnchor.GetCurrentCycleAnchor(show, nowLocal, settings);
            skipReason =
                $"Already refreshed for current cycle (anchor {cycleAnchor:yyyy-MM-dd HH:mm}, next {cycleAnchor.AddDays(7):yyyy-MM-dd HH:mm})";
            return false;
        }

        return true;
    }

    public static bool ShouldResetDormantState(TrackedShow show, AutoTrackSettings settings, DateTime nowLocal)
    {
        return show.AutoTrackTmdbState == AutoTrackTmdbState.DormantCaughtUp &&
               !HasSatisfiedCurrentCycle(show, settings, nowLocal);
    }

    /// <summary>
    /// True when the show was refreshed at or after its most recent anchor. A missed anchor
    /// (app closed, network down) stays unsatisfied until a refresh succeeds.
    /// </summary>
    public static bool HasSatisfiedCurrentCycle(
        TrackedShow show,
        AutoTrackSettings settings,
        DateTime nowLocal)
    {
        if (!AutoTrackWeekAnchor.IsWeeklyScheduleEnforced(show, settings))
        {
            return false;
        }

        if (show.AutoTrackLastTmdbRefreshLocal is null)
        {
            return false;
        }

        var cycleAnchor = AutoTrackWeekAnchor.GetCurrentCycleAnchor(show, nowLocal, settings);
        return show.AutoTrackLastTmdbRefreshLocal.Value >= cycleAnchor;
    }

    /// <summary>
    /// Hunt gate for scheduled shows: only episodes that aired by the current cycle anchor.
    /// </summary>
    public static IReadOnlyList<TrackedEpisode> FilterToCycle(
        IReadOnlyList<TrackedEpisode> pending,
        DateTime cycleAnchor)
    {
        return pending
            .Where(episode => episode.AirDate is null || episode.AirDate.Value <= cycleAnchor)
            .ToList();
    }

    public static bool IsFullyCaughtUp(TrackedShow show, IReadOnlyList<TrackedEpisode> episodes)
    {
        if (!show.IsAutoTracked)
        {
            return true;
        }

        var fromSeason = show.AutoTrackFromSeason!.Value;
        var fromEpisode = show.AutoTrackFromEpisode!.Value;

        return episodes
            .Where(episode => IsAtOrAfterCheckpoint(episode, fromSeason, fromEpisode))
            .All(episode => episode.Availability == EpisodeAvailability.Available);
    }

    public static bool HasPendingLatestEpisode(
        TrackedShow show,
        IReadOnlyList<TrackedEpisode> episodes,
        Func<long, bool> hasActiveCartOrder,
        DateTime? nowLocal = null,
        int huntDelayHours = 0)
    {
        return FindPendingEpisodes(show, episodes, hasActiveCartOrder, nowLocal, huntDelayHours).Count > 0;
    }

    public static TrackedEpisode? FindLatestPendingEpisode(
        TrackedShow show,
        IReadOnlyList<TrackedEpisode> episodes,
        Func<long, bool> hasActiveCartOrder,
        DateTime? nowLocal = null,
        int huntDelayHours = 0)
    {
        var pending = FindPendingEpisodes(show, episodes, hasActiveCartOrder, nowLocal, huntDelayHours);
        return pending.Count == 0 ? null : pending[^1];
    }

    public static IReadOnlyList<TrackedEpisode> FindPendingEpisodes(
        TrackedShow show,
        IReadOnlyList<TrackedEpisode> episodes,
        Func<long, bool> hasActiveCartOrder,
        DateTime? nowLocal = null,
        int huntDelayHours = 0)
    {
        if (!show.IsAutoTracked)
        {
            return [];
        }

        var fromSeason = show.AutoTrackFromSeason!.Value;
        var fromEpisode = show.AutoTrackFromEpisode!.Value;
        var now = nowLocal ?? DateTime.Now;
        var delayHours = Math.Max(0, huntDelayHours);

        return episodes
            .Where(episode => IsAtOrAfterCheckpoint(episode, fromSeason, fromEpisode))
            .Where(episode => episode.AirDate is null || episode.AirDate.Value.AddHours(delayHours) <= now)
            .Where(episode => episode.Availability == EpisodeAvailability.Missing)
            .Where(episode => string.IsNullOrWhiteSpace(episode.TorrentHash))
            .Where(episode => !hasActiveCartOrder(episode.Id))
            .OrderBy(episode => episode.SeasonNumber)
            .ThenBy(episode => episode.EpisodeNumber)
            .ToList();
    }

    private static bool IsAtOrAfterCheckpoint(TrackedEpisode episode, int fromSeason, int fromEpisode)
    {
        return episode.SeasonNumber > fromSeason ||
               (episode.SeasonNumber == fromSeason && episode.EpisodeNumber >= fromEpisode);
    }

    public static string FormatHuntStatusLine(IReadOnlyList<TrackedEpisode> episodes)
    {
        if (episodes.Count == 0)
        {
            return string.Empty;
        }

        var first = episodes[0];
        if (episodes.Count == 1)
        {
            return $"Hunting: S{first.SeasonNumber:00}E{first.EpisodeNumber:00}";
        }

        var last = episodes[^1];
        return $"Hunting: S{first.SeasonNumber:00}E{first.EpisodeNumber:00}–S{last.SeasonNumber:00}E{last.EpisodeNumber:00} ({episodes.Count})";
    }
}
