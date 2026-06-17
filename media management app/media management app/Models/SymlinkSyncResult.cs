namespace media_management_app.Models;

public sealed class SymlinkSyncResult
{
    public int CreatedCount { get; set; }

    public int RepairedCount { get; set; }

    public int RemovedCount { get; set; }

    public int SkippedCount { get; set; }

    public int ErrorCount { get; set; }

    public List<string> Messages { get; } = [];

    public string Summary =>
        $"Symlinks: created {CreatedCount}, repaired {RepairedCount}, removed {RemovedCount}, skipped {SkippedCount}, errors {ErrorCount}.";
}
