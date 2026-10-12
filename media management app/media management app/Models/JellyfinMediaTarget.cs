namespace media_management_app.Models;

public sealed class JellyfinMediaTarget
{
    public JellyfinMediaKind Kind { get; init; }

    public int TmdbId { get; init; }

    public int? SeasonNumber { get; init; }

    public int? EpisodeNumber { get; init; }

    public string? DisplayName { get; init; }

    public static JellyfinMediaTarget FromEpisode(JellyfinEpisodeTarget episode, string? displayName = null) =>
        new()
        {
            Kind = JellyfinMediaKind.Episode,
            TmdbId = episode.ShowTmdbId,
            SeasonNumber = episode.SeasonNumber,
            EpisodeNumber = episode.EpisodeNumber,
            DisplayName = displayName
        };
}
