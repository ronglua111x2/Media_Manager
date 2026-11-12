namespace media_management_app.Models;

public sealed class SpecialMappingItem
{
    public string RelativePath { get; init; } = string.Empty;

    public TrackedEpisode? Episode { get; init; }

    public SpecialMappingSource Source { get; init; }

    public SpecialMappingProposalReason? ProposalReason { get; init; }

    public string Reason { get; init; } = string.Empty;

    /// <summary>The file's own title matches the mapped TMDB title.</summary>
    public bool TitleConfirmed { get; init; }

    /// <summary>A rule guess for an unmapped file (k-th → k-th of an inexact season window). Shown in review, never linked.</summary>
    public TrackedEpisode? SuggestedEpisode { get; init; }

    /// <summary>
    /// For a Gemini pair on a file that also had a rule suggestion: true when both give the same episode, false when
    /// they differ (the review window highlights the row), null when there was no suggestion.
    /// </summary>
    public bool? MatchesSuggestion { get; init; }
}

public sealed class SpecialMappingResult
{
    public IReadOnlyList<SpecialMappingItem> Items { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool UsedGemini { get; init; }

    public IReadOnlyDictionary<string, SpecialMappingItem> ByPath { get; init; } =
        new Dictionary<string, SpecialMappingItem>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Set when the special step itself found a problem (Gemini failed and rules were used instead, or
    /// conflicting pairs were dropped). The pack link must then be reviewed before anything is linked.
    /// </summary>
    public bool RequiresReview { get; init; }

    /// <summary>Why the rule mappings replace the requested Gemini mappings, or null when nothing failed.</summary>
    public string? FallbackReason { get; init; }

    /// <summary>Copy with extra warnings and the review flag set (items are unchanged).</summary>
    public SpecialMappingResult WithReview(IEnumerable<string> extraWarnings, string? fallbackReason = null) =>
        new()
        {
            Items = Items,
            Warnings = Warnings.Concat(extraWarnings).Distinct(StringComparer.Ordinal).ToList(),
            UsedGemini = UsedGemini,
            ByPath = ByPath,
            RequiresReview = true,
            FallbackReason = fallbackReason ?? FallbackReason
        };
}
