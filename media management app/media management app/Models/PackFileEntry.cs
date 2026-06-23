namespace media_management_app.Models;

public sealed class PackFileEntry
{
    public string RelativePath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public PackFileClassification Classification { get; init; }

    public int? MatchedSeasonNumber { get; init; }

    public int? MatchedEpisodeNumber { get; init; }

    public string MatchReason { get; init; } = string.Empty;

    public SpecialMappingSource? MappingSource { get; init; }

    public SpecialMappingProposalReason? ProposalReason { get; init; }
}
