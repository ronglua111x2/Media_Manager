namespace media_management_app.Models;

public sealed class SpecialMappingItem
{
    public string RelativePath { get; init; } = string.Empty;

    public TrackedEpisode? Episode { get; init; }

    public SpecialMappingSource Source { get; init; }

    public SpecialMappingProposalReason? ProposalReason { get; init; }

    public string Reason { get; init; } = string.Empty;
}

public sealed class SpecialMappingResult
{
    public IReadOnlyList<SpecialMappingItem> Items { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool UsedGemini { get; init; }

    public IReadOnlyDictionary<string, SpecialMappingItem> ByPath { get; init; } =
        new Dictionary<string, SpecialMappingItem>(StringComparer.OrdinalIgnoreCase);
}
