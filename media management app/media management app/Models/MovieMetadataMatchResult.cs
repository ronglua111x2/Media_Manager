namespace media_management_app.Models;

public sealed class MovieMetadataMatchResult
{
    public bool IsAvailable { get; set; }

    public string? ErrorMessage { get; set; }

    public List<TmdbMovieCandidate> Candidates { get; set; } = [];

    public TmdbMovieCandidate? BestCandidate { get; set; }
}
