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

        if (!bypassAnchor && show.AutoTrackLastTmdbWeekKey == AutoTrackWeekAnchor.WeekKey(nowLocal))
        {
            return false;
        }

        if (show.SeriesStatus == ShowSeriesStatus.Finished && IsFullyCaughtUp(show, episodes))
        {
            return false;
        }

        if (!bypassAnchor && show.AutoTrackTmdbState == AutoTrackTmdbState.DormantCaughtUp)
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
               show.AutoTrackLastTmdbWeekKey != AutoTrackWeekAnchor.WeekKey(nowLocal);
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
        DateTime? nowLocal = null)
    {
        return FindLatestPendingEpisode(show, episodes, hasActiveCartOrder, nowLocal) is not null;
    }

    public static TrackedEpisode? FindLatestPendingEpisode(
        TrackedShow show,
        IReadOnlyList<TrackedEpisode> episodes,
        Func<long, bool> hasActiveCartOrder,
        DateTime? nowLocal = null)
    {
        if (!show.IsAutoTracked)
        {
            return null;
        }

        var fromSeason = show.AutoTrackFromSeason!.Value;
        var fromEpisode = show.AutoTrackFromEpisode!.Value;
        var today = (nowLocal ?? DateTime.Now).Date;

        return episodes
            .Where(episode => IsAtOrAfterCheckpoint(episode, fromSeason, fromEpisode))
            .Where(episode => episode.AirDate is null || episode.AirDate.Value.Date <= today)
            .Where(episode => episode.Availability == EpisodeAvailability.Missing)
            .Where(episode => string.IsNullOrWhiteSpace(episode.TorrentHash))
            .Where(episode => !hasActiveCartOrder(episode.Id))
            .OrderByDescending(episode => episode.SeasonNumber)
            .ThenByDescending(episode => episode.EpisodeNumber)
            .FirstOrDefault();
    }

    private static bool IsAtOrAfterCheckpoint(TrackedEpisode episode, int fromSeason, int fromEpisode)
    {
        return episode.SeasonNumber > fromSeason ||
               (episode.SeasonNumber == fromSeason && episode.EpisodeNumber >= fromEpisode);
    }
}
