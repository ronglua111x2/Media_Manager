namespace media_management_app.Models;

public sealed class TmdbSeasonDetails
{
    public int SeasonNumber { get; set; }

    public int EpisodeCount { get; set; }

    public List<TmdbEpisodeDetails> Episodes { get; set; } = [];
}

