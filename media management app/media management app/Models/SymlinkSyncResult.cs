namespace media_management_app.Models;

public sealed class SymlinkSyncResult
{
    public int CreatedCount { get; set; }

    public int RepairedCount { get; set; }

    public int RemovedCount { get; set; }

    public int SkippedCount { get; set; }

    public int ErrorCount { get; set; }

    public List<string> Messages { get; } = [];

    /// <summary>Symlink paths created or repaired in this result (for Jellyfin path notify).</summary>
    public List<string> TouchedSymlinkPaths { get; } = [];

    public string Summary =>
        $"Symlinks: created {CreatedCount}, repaired {RepairedCount}, removed {RemovedCount}, skipped {SkippedCount}, errors {ErrorCount}.";
}
