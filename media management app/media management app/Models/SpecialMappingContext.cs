namespace media_management_app.Models;

public sealed class SpecialMappingContext
{
    public string ShowTitle { get; init; } = string.Empty;

    public int TmdbId { get; init; }

    public IReadOnlyList<(int ParentSeason, int FileCount, IReadOnlyList<int> LocalIndices)> Blocks { get; init; } =
        [];

    public IReadOnlyList<SpecialMappingCandidate> Candidates { get; init; } = [];

    public IReadOnlyList<(int Episode, string AirDate, string Title)> TmdbSpecials { get; init; } = [];

    public int CandidateCount { get; init; }

    public int TmdbSpecialCount { get; init; }

    public bool CountMismatch { get; init; }

    public bool HasOpaqueNames { get; init; }
}
