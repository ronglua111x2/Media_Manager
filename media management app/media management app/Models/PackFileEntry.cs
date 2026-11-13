namespace media_management_app.Models;

public sealed class PackFileEntry
{
    public string RelativePath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public PackFileClassification Classification { get; init; }

    public int? MatchedSeasonNumber { get; init; }

    public int? MatchedEpisodeNumber { get; init; }

    /// <summary>Last episode of a multi-episode file (S01E01-E02); null for one episode.</summary>
    public int? MatchedLastEpisodeNumber { get; init; }

    public string MatchReason { get; init; } = string.Empty;

    public SpecialMappingSource? MappingSource { get; init; }

    public SpecialMappingProposalReason? ProposalReason { get; init; }
}
