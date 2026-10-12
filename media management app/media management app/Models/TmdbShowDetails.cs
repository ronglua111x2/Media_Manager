namespace media_management_app.Models;

public sealed class TmdbShowDetails
{
    public int TmdbId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? FirstAirYear { get; set; }

    public string? Overview { get; set; }

    public string? PosterPath { get; set; }

    public int SeasonCount { get; set; }

    public int EpisodeCount { get; set; }

    /// <summary>Sum of seasons[].episode_count for season_number &gt;= 1, or fallback to number_of_episodes.</summary>
    public int PlannedEpisodeCount { get; set; }

    public Common.ShowSeriesStatus SeriesStatus { get; set; } = Common.ShowSeriesStatus.Unknown;

    public List<TmdbSeasonDetails> Seasons { get; set; } = [];

    public List<string> AlternativeTitles { get; set; } = [];
}
