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

    public Common.ShowSeriesStatus SeriesStatus { get; set; } = Common.ShowSeriesStatus.Unknown;

    public List<TmdbSeasonDetails> Seasons { get; set; } = [];
}
