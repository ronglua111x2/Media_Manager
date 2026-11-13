namespace media_management_app.Models;

public sealed class TmdbEpisodeDetails
{
    /// <summary>TMDB's own id of the episode record; it stays when TMDB renumbers the episode (audit L3-09).</summary>
    public int? TmdbEpisodeId { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime? AirDate { get; set; }

    public string? Overview { get; set; }

    public double? VoteAverage { get; set; }

    public string? StillPath { get; set; }
}

