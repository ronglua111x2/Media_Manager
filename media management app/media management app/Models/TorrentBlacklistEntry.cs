namespace media_management_app.Models;

/// <summary>
/// Blacklisted malicious torrent listing for a single tracked show/movie.
/// </summary>
public sealed class TorrentBlacklistEntry
{
    public long Id { get; set; }

    /// <summary>
    /// Search listing URL (HTML details page or magnet). Primary key at rank time.
    /// </summary>
    public string ListingUrl { get; set; } = string.Empty;

    /// <summary>
    /// Infohash when known (from magnet btih or after qBittorrent add).
    /// </summary>
    public string InfoHash { get; set; } = string.Empty;

    public long ShowId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? SuspiciousFilesJson { get; set; }

    public DateTime DateAddedUtc { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}
