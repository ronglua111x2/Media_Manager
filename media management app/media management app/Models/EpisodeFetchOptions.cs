namespace media_management_app.Models;

public sealed class EpisodeFetchOptions
{
    public bool? ForceParallelEpisodeSearch { get; init; }

    public int? MaxParallelWorkers { get; init; }

    public RecipeExecutionOverrides? Overrides { get; init; }
}
