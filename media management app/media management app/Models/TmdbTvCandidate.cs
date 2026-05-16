namespace media_management_app.Models;

public sealed class TmdbTvCandidate
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? OriginalName { get; set; }

    public string? FirstAirDate { get; set; }

    public int? FirstAirYear { get; set; }

    public string? OriginalLanguage { get; set; }

    public int? NumberOfEpisodes { get; set; }

    public int? NumberOfSeasons { get; set; }

    public List<string> Genres { get; set; } = [];

    public double Confidence { get; set; }

    public string MatchReason { get; set; } = string.Empty;
}
