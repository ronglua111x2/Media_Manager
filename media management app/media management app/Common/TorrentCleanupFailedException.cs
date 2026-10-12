namespace media_management_app.Common;

/// <summary>
/// Thrown when a rejected torrent could not be verified as deleted from qBittorrent.
/// Callers must halt the current add batch; do not treat this as a successful malware reject.
/// </summary>
public sealed class TorrentCleanupFailedException : Exception
{
    public TorrentCleanupFailedException(string torrentHash, string cleanupReason, Exception? innerException = null)
        : base(FormatMessage(torrentHash, cleanupReason), innerException)
    {
        TorrentHash = torrentHash ?? string.Empty;
        CleanupReason = cleanupReason ?? string.Empty;
    }

    public string TorrentHash { get; }

    public string CleanupReason { get; }

    public string OrderStatusDetail =>
        $"Cleanup failed. Hash={TorrentHash}. Inspect qBittorrent. The torrent was paused if possible.";

    private static string FormatMessage(string torrentHash, string cleanupReason)
    {
        var hash = string.IsNullOrWhiteSpace(torrentHash) ? "(unknown)" : torrentHash.Trim();
        var reason = string.IsNullOrWhiteSpace(cleanupReason) ? "cleanup" : cleanupReason.Trim();
        return $"Rejected torrent '{hash}' could not be deleted from qBittorrent ({reason}). Inspect qBittorrent; the torrent was paused if possible.";
    }
}
