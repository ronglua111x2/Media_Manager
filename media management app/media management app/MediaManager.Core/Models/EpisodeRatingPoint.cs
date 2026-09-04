namespace media_management_app.Models;

public sealed class EpisodeRatingPoint
{
    public int SeasonNumber { get; init; }

    public int EpisodeNumber { get; init; }

    public double? UserRating { get; init; }

    public double? VoteAverage { get; init; }
}
