using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Infers a show's weekly broadcast weekday from episode air dates (calendar dates in UTC+7).
/// </summary>
public static class ShowWeeklyAirDay
{
    private const int RecentEpisodeCap = 12;

    public static DayOfWeek? Infer(
        IReadOnlyList<TrackedEpisode> episodes,
        int? fromSeason = null,
        int? fromEpisode = null)
    {
        IEnumerable<TrackedEpisode> query = episodes
            .Where(episode => episode.AirDate is not null)
            .Where(episode => episode.SeasonNumber != AppConstants.SpecialsSeasonNumber);

        if (fromSeason is not null && fromEpisode is not null)
        {
            query = query.Where(episode =>
                episode.SeasonNumber > fromSeason.Value ||
                (episode.SeasonNumber == fromSeason.Value && episode.EpisodeNumber >= fromEpisode.Value));
        }

        var recentDays = query
            .OrderByDescending(episode => episode.AirDate!.Value.Date)
            .ThenByDescending(episode => episode.SeasonNumber)
            .ThenByDescending(episode => episode.EpisodeNumber)
            .Take(RecentEpisodeCap)
            .Select(episode => ToUtcPlus7CalendarDay(episode.AirDate!.Value).DayOfWeek)
            .ToList();

        if (recentDays.Count == 0)
        {
            return null;
        }

        var ranked = recentDays
            .GroupBy(day => day)
            .Select(group => (Day: group.Key, Count: group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Day)
            .ToList();

        if (ranked.Count == 0)
        {
            return null;
        }

        // Strict plurality: unique highest count.
        if (ranked.Count > 1 && ranked[0].Count == ranked[1].Count)
        {
            return null;
        }

        return ranked[0].Day;
    }

    public static string? FormatLabel(DayOfWeek? day) =>
        day?.ToString();

    /// <summary>
    /// Air dates are date-only; treat the calendar date as UTC+7 local midnight so DayOfWeek
    /// matches the app clock region.
    /// </summary>
    private static DateTime ToUtcPlus7CalendarDay(DateTime airDate)
    {
        var date = airDate.Date;
        return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
    }
}
