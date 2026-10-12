namespace media_management_app.Models;

public sealed class EpisodeUserRatingRow
{
    public long ShowId { get; init; }

    public int SeasonNumber { get; init; }

    public int EpisodeNumber { get; init; }

    public string Title { get; init; } = string.Empty;

    public double? UserRating { get; init; }

    public string? Thought { get; init; }

    public bool IsHidden { get; init; }
}
