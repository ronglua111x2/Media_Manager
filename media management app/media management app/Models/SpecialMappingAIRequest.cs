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
}
