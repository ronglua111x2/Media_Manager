namespace media_management_app.Models;

public sealed class AutoTorrentLinkResult
{
    public int LinkedCount { get; set; }

    public int SkippedCount { get; set; }

    public int RemovedCount { get; set; }

    public int ErrorCount { get; set; }

    public List<string> Messages { get; } = [];

    public string Summary => $"Linked={LinkedCount}, Skipped={SkippedCount}, Removed={RemovedCount}, Errors={ErrorCount}";
}
