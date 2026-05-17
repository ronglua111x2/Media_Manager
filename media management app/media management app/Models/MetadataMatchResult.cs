namespace media_management_app.Models;

public sealed class MetadataMatchResult
{
    public bool IsAvailable { get; set; }

    public string? ErrorMessage { get; set; }

    public List<TmdbTvCandidate> Candidates { get; set; } = [];

    public TmdbTvCandidate? BestCandidate { get; set; }
}
