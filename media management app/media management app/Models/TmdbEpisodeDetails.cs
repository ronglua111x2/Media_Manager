namespace media_management_app.Models;

public sealed class TmdbEpisodeDetails
{
    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime? AirDate { get; set; }
}

