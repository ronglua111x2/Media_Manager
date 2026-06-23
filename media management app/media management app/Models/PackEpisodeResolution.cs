namespace media_management_app.Models;

public enum PackEpisodeResolutionStatus
{
    RegularEpisode,
    Skipped
}

public sealed class PackEpisodeResolution
{
    public PackEpisodeResolutionStatus Status { get; init; }

    public int SeasonNumber { get; init; }

    public int? EpisodeNumber { get; init; }

    public string? SkipReason { get; init; }

    public static PackEpisodeResolution Linked(int seasonNumber, int episodeNumber) =>
        new()
        {
            Status = PackEpisodeResolutionStatus.RegularEpisode,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber
        };

    public static PackEpisodeResolution Skipped(int seasonNumber, string reason) =>
        new()
        {
            Status = PackEpisodeResolutionStatus.Skipped,
            SeasonNumber = seasonNumber,
            SkipReason = reason
        };
}
