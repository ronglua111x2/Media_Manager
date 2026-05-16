namespace media_management_app.Models;

public sealed class ParsedCandidate
{
    public string SourceText { get; set; } = string.Empty;

    public string? ShowTitle { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public string? EpisodeTitle { get; set; }

    public bool NeedsReview { get; set; }

    public string? Reason { get; set; }
}
