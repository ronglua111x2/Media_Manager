namespace media_management_app.Models;

public sealed class JellyfinEpisodeTarget
{
    public int ShowTmdbId { get; init; }

    public int SeasonNumber { get; init; }

    public int EpisodeNumber { get; init; }
}
