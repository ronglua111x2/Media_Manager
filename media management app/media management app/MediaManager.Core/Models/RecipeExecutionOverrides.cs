namespace media_management_app.Models;

public sealed class RecipeExecutionOverrides
{
    public int? MaxCandidates { get; init; }

    public int? MinSeeders { get; init; }

    public bool OverrideMinSize { get; init; }

    public long? MinSizeBytes { get; init; }

    public bool? EnableCandidateDebugLog { get; init; }

    public bool HasFilterOverrides =>
        MinSeeders is not null || OverrideMinSize;

    public bool HasRecipeCloneOverrides =>
        HasFilterOverrides || EnableCandidateDebugLog is not null;
}
