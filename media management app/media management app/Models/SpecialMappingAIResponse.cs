namespace media_management_app.Models;

public sealed class SpecialMappingAIResponse
{
    public IReadOnlyList<(int CandidateIndex, int S00E)> Mappings { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
