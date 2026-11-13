namespace media_management_app.Models;

public sealed class TorrentReconciliationResult
{
    public int MatchedCount { get; set; }

    public int CompletedCount { get; set; }

    public int LinkedCount { get; set; }

    public int MissingCount { get; set; }

    /// <summary>
    /// Names of qBittorrent torrents matched by name but not attached because the content check or the blacklist
    /// refused them (SEC-A01). They are left in qBittorrent.
    /// </summary>
    public List<string> UnsafeSkipped { get; } = [];

    public int UnsafeSkippedCount => UnsafeSkipped.Count;

    public string Summary =>
        $"Matched={MatchedCount}, Completed={CompletedCount}, Linked={LinkedCount}, Missing={MissingCount}" +
        (UnsafeSkippedCount > 0 ? $", Unsafe (not attached, see log)={UnsafeSkippedCount}" : string.Empty);
}
