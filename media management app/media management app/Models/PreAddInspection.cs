namespace media_management_app.Models;

/// <summary>
/// S0 (SEC-A03): a torrent's file list read before it is added to qBittorrent, so the add gate can reject it without
/// a single payload byte on disk. Either qBittorrent fetched the metadata of a magnet (WebAPI ≥ 2.11.9,
/// <see cref="MetadataPrefetched"/>; adding <see cref="AddUrl"/> then reuses that metadata and takes file priorities),
/// or the app downloaded the .torrent itself (<see cref="TorrentBytes"/>, added as is to avoid a second download).
/// </summary>
public sealed record PreAddInspection
{
    /// <summary>What to add: the magnet whose metadata qBittorrent holds, or the URL the .torrent came from.</summary>
    public required string AddUrl { get; init; }

    /// <summary>The .torrent that was checked; the add uploads exactly these bytes.</summary>
    public byte[]? TorrentBytes { get; init; }

    public string TorrentFileName { get; init; } = "download.torrent";

    /// <summary>v1 infohash (lower-case hex) when known.</summary>
    public string? InfoHash { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Files in qBittorrent's file-index order.</summary>
    public IReadOnlyList<TorrentContentFile> Files { get; init; } = [];

    /// <summary>qBittorrent holds the metadata (fetchMetadata): the add can carry file priorities.</summary>
    public bool MetadataPrefetched { get; init; }

    /// <summary>qBittorrent could not fetch the magnet's metadata in time; nothing was added.</summary>
    public bool MetadataTimedOut { get; init; }
}
