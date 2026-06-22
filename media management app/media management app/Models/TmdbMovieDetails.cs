namespace media_management_app.Models;

public sealed class TmdbMovieDetails
{
    public int TmdbId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? ReleaseYear { get; set; }

    public string? Overview { get; set; }

    public string? PosterPath { get; set; }

    public int? RuntimeMinutes { get; set; }

    public List<string> AlternativeTitles { get; set; } = [];
}
