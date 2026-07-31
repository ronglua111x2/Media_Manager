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
        bool bypassAnchor = false)
    {
        if (!show.IsAutoTracked)
        {
            return false;
        }

        if (!bypassAnchor && !AutoTrackWeekAnchor.IsPastAnchorThisWeek(show, nowLocal, settings))
        {
            return false;
        }

        if (!bypassAnchor && HasSatisfiedPostAnchorRefreshThisWeek(show, settings, nowLocal))
        {
            return false;
        }

        if (show.SeriesStatus == ShowSeriesStatus.Finished && IsFullyCaughtUp(show, episodes))
        {
            return false;
        }

        if (!bypassAnchor && show.AutoTrackTmdbState == AutoTrackTmdbState.FinishedComplete)
        {
            return false;
        }

        return true;
    }

    public static bool ShouldResetDormantState(TrackedShow show, AutoTrackSettings settings, DateTime nowLocal)
    {
        return show.AutoTrackTmdbState == AutoTrackTmdbState.DormantCaughtUp &&
               AutoTrackWeekAnchor.IsPastAnchorThisWeek(show, nowLocal, settings) &&
               !HasSatisfiedPostAnchorRefreshThisWeek(show, settings, nowLocal);
    }

    public static bool HasSatisfiedPostAnchorRefreshThisWeek(
        TrackedShow show,
        AutoTrackSettings settings,
        DateTime nowLocal)
    {
        if (show.AutoTrackLastTmdbRefreshLocal is null)
        {
            return false;
        }

        var (day, time) = AutoTrackWeekAnchor.GetEffectiveAnchor(show, settings);
        var anchorThisWeek = AutoTrackWeekAnchor.GetAnchorDateTimeThisWeek(day, time, nowLocal);
        return show.AutoTrackLastTmdbRefreshLocal.Value >= anchorThisWeek;
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
