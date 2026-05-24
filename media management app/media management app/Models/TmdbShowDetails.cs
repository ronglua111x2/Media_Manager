namespace media_management_app.Models;

public sealed class TmdbShowDetails
{
    public int TmdbId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? FirstAirYear { get; set; }

    public string? Overview { get; set; }

    public string? PosterPath { get; set; }

    public List<TmdbSeasonDetails> Seasons { get; set; } = [];
}

