namespace media_management_app.Models;

public sealed class EpisodeMappingResult
{
    public bool IsAvailable { get; set; }

    public bool IsMapped { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public string? Source { get; set; }

    public double? Confidence { get; set; }

    public string? Reason { get; set; }

    public string? ErrorMessage { get; set; }
}
