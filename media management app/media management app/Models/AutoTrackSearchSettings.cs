namespace media_management_app.Models;

public sealed class AutoTrackSearchSettings
{
    public int MaxShowsPerHuntCycle { get; set; } = 3;

    public int MaxEpisodesPerShowPerHuntCycle { get; set; } = 5;

    public int MaxParallelWorkersPerShow { get; set; } = 1;

    public bool ForceParallelEpisodeSearch { get; set; } = true;
}
