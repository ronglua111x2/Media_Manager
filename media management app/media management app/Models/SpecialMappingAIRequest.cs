namespace media_management_app.Models;

public sealed class SpecialMappingAIRequest
{
    public SpecialMappingContext Context { get; init; } = new();

    public IReadOnlyList<SpecialMappingProposal> Proposals { get; init; } = [];

    public string Task { get; init; } = "finalize";
}
