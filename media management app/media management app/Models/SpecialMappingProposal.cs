namespace media_management_app.Models;

public sealed class SpecialMappingProposal
{
    public int CandidateIndex { get; init; }

    public int ProposedS00E { get; init; }

    public SpecialMappingProposalReason Reason { get; init; }
}
