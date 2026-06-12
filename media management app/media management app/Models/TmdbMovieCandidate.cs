namespace media_management_app.Models;

public sealed class TmdbMovieCandidate
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? ReleaseYear { get; set; }

    public string? Overview { get; set; }

    public string? PosterPath { get; set; }

    public double Confidence { get; set; }

    public string MatchReason { get; set; } = string.Empty;

    public string DisplayTitle => ReleaseYear is null ? Title : $"{Title} ({ReleaseYear})";
}
