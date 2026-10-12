namespace media_management_app.Models;

public sealed class TorrentMetadataProbeResult
{
    public bool IsAvailable { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string TorrentName { get; init; } = string.Empty;

    public long TotalSize { get; init; }

    public IReadOnlyList<TorrentMetadataFile> Files { get; init; } = [];

    public IReadOnlyList<TorrentMetadataFile> VideoFiles => Files
        .Where(file => IsVideoFile(file.Path))
        .ToList();

    public int VideoFileCount => VideoFiles.Count;

    private static bool IsVideoFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mov", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class TorrentMetadataFile
{
    public string Path { get; init; } = string.Empty;

    public long Length { get; init; }
}
