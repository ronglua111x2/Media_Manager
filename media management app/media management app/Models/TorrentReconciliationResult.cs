namespace media_management_app.Models;

public sealed class TorrentReconciliationResult
{
    public int MatchedCount { get; set; }

    public int CompletedCount { get; set; }

    public int LinkedCount { get; set; }

    public int MissingCount { get; set; }

    public string Summary =>
        $"Matched={MatchedCount}, Completed={CompletedCount}, Linked={LinkedCount}, Missing={MissingCount}";
}
