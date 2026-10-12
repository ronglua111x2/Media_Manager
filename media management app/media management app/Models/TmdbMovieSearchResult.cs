namespace media_management_app.Models;

public sealed class TmdbMovieSearchResult
{
    public int TmdbId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? ReleaseYear { get; set; }

    public string? Overview { get; set; }

    public string? PosterPath { get; set; }

    public string DisplayTitle => ReleaseYear is null ? Title : $"{Title} ({ReleaseYear})";
}
