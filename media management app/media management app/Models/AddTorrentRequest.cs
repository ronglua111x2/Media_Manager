namespace media_management_app.Models;

public sealed class AddTorrentRequest
{
    public string Url { get; init; } = string.Empty;

    public string PluginName { get; init; } = string.Empty;

    public string SavePath { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Tags { get; init; } = "media-manager";

    /// <summary>Add stopped (<c>paused</c> + <c>stopped</c>). Used for a .torrent whose metadata is already known.</summary>
    public bool Paused { get; init; }

    /// <summary>
    /// S0: add with <c>stopCondition=MetadataReceived</c> (WebAPI ≥ 2.8.18): a magnet fetches its file list and stops
    /// before downloading. A search-plugin add, which cannot take the condition, is stopped right after it appears.
    /// </summary>
    public bool StopAfterMetadata { get; init; }

    /// <summary>S0: upload these .torrent bytes instead of resolving <see cref="Url"/> again (no TOCTOU).</summary>
    public byte[]? TorrentBytes { get; init; }

    public string TorrentFileName { get; init; } = "download.torrent";

    /// <summary>
    /// S0: per-file priorities (0 = do not download) for a magnet whose metadata qBittorrent already fetched;
    /// adds <see cref="Url"/> directly, without the plugin or the HTML resolver.
    /// </summary>
    public IReadOnlyList<int>? FilePriorities { get; init; }
}
