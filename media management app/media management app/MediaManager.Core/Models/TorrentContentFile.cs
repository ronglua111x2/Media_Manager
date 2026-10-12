namespace media_management_app.Models;

public sealed class TorrentContentFile
{
    public string Name { get; init; } = string.Empty;

    public long Size { get; init; }

    public double Progress { get; init; }

    public int Priority { get; init; }

    public bool IsSeed { get; init; }

    public bool IsComplete => Progress >= 0.999;

    public bool IsVideoFile
    {
        get
        {
            var extension = Path.GetExtension(Name);
            return extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".wmv", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase);
        }
    }
}
