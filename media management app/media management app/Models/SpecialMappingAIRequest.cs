namespace media_management_app.Models;

public sealed class SpecialMappingAIRequest
{
    public SpecialMappingContext Context { get; init; } = new();

    public IReadOnlyList<SpecialMappingProposal> Proposals { get; init; } = [];

    public string Task { get; init; } = "finalize";

    /// <summary>Review-only guesses for unmapped candidates, by candidate index. Never sent to Gemini.</summary>
    public IReadOnlyDictionary<int, TrackedEpisode> Suggestions { get; init; } = new Dictionary<int, TrackedEpisode>();

    /// <summary>Why the deterministic tier left a candidate unmapped, by candidate index. Never sent to Gemini.</summary>
    public IReadOnlyDictionary<int, string> UnresolvedReasons { get; init; } = new Dictionary<int, string>();

    /// <summary>
    /// Candidates the deterministic tier left open, as indices into <see cref="SpecialMappingContext.Candidates"/>,
    /// in the order they are sent to Gemini (by season, numbered files by number, unnumbered files last).
    /// Position k in this list is Gemini's candidateIndex k. Empty: the rules placed every file and Gemini is not called.
    /// </summary>
    public IReadOnlyList<int> GeminiCandidateIndices { get; init; } = [];

    /// <summary>TMDB Season 0 split by the regular seasons' air-date windows (empty without air dates).</summary>
    public IReadOnlyList<Services.SpecialSeasonBlock> SeasonBlocks { get; init; } = [];
}
