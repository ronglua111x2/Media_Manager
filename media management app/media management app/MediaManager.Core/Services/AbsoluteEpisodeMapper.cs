namespace media_management_app.Services;

/// <summary>
/// Converts an absolute episode number (anime batches: "Show - 13") to a TMDB season/episode using
/// the show's per-season episode counts. Season 0 never takes part.
/// </summary>
public sealed class AbsoluteEpisodeMapper
{
    private readonly IReadOnlyList<(int Season, int Count)> _seasons;

    public AbsoluteEpisodeMapper(IReadOnlyDictionary<int, int> episodeCountsBySeason)
    {
        _seasons = episodeCountsBySeason
            .Where(pair => pair.Key > 0 && pair.Value > 0)
            .OrderBy(pair => pair.Key)
            .Select(pair => (pair.Key, pair.Value))
            .ToList();
    }

    /// <summary>Highest regular season TMDB lists for the show; 0 when unknown.</summary>
    public int MaxSeasonNumber => _seasons.Count == 0 ? 0 : _seasons[^1].Season;

    /// <summary>
    /// A number that is already an episode of <paramref name="groupSeason"/> stays in that season
    /// (season-relative numbering, or TMDB seasons that continue the numbering). Otherwise the
    /// number is counted through the seasons in order: with S1 = 12 episodes, 13 is S02E01.
    /// </summary>
    public bool TryMap(
        int absoluteEpisode,
        int groupSeason,
        IReadOnlySet<int> groupSeasonEpisodes,
        out int seasonNumber,
        out int episodeNumber,
        out string? failureReason)
    {
        seasonNumber = 0;
        episodeNumber = 0;
        failureReason = null;

        if (absoluteEpisode <= 0)
        {
            failureReason = $"Absolute episode {absoluteEpisode} is not a valid number.";
            return false;
        }

        if (groupSeason > 0 && groupSeasonEpisodes.Contains(absoluteEpisode))
        {
            seasonNumber = groupSeason;
            episodeNumber = absoluteEpisode;
            return true;
        }

        var offset = 0;
        foreach (var (season, count) in _seasons)
        {
            if (absoluteEpisode <= offset + count)
            {
                seasonNumber = season;
                episodeNumber = absoluteEpisode - offset;
                return true;
            }

            offset += count;
        }

        failureReason = _seasons.Count == 0
            ? $"Absolute episode {absoluteEpisode}: no TMDB season episode counts."
            : $"Absolute episode {absoluteEpisode} is beyond the show's {offset} TMDB episodes.";
        return false;
    }
}
