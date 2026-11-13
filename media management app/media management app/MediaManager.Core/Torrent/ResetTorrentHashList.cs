namespace media_management_app.Services;

/// <summary>
/// The torrents an episode, season pack or movie was reset from ("Reset for redownload"), stored as one
/// comma-separated, lower-case list in a <c>ResetTorrentHashes</c> column (migration 013). The reset keeps the torrent
/// in qBittorrent, so without this list the next reconcile adopted it again by name and the reset was undone
/// (cross-review 2, B). Every hash is kept: each reset rejected that download. Not the blacklist: a blacklisted hash
/// means "malicious" and the add gate deletes it with its files.
/// </summary>
public static class ResetTorrentHashList
{
    public static IReadOnlyList<string> Parse(string? list) =>
        string.IsNullOrWhiteSpace(list)
            ? []
            : list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Contains(string? list, string? hash) =>
        !string.IsNullOrWhiteSpace(hash) &&
        Parse(list).Contains(hash.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string hash) => hash.Trim().ToLowerInvariant();
}
