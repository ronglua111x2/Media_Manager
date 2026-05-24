namespace media_management_app.Models;

public sealed class EpisodeFetchCandidate
{
    public long EpisodeId { get; set; }

    public long MovieId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string FileSizeDisplay => FormatSize(FileSize);

    public string FileUrl { get; set; } = string.Empty;

    public string QualityLabel { get; set; } = string.Empty;

    public string AudioCodecLabel { get; set; } = string.Empty;

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public string PluginName { get; set; } = string.Empty;

    public int QualityScore { get; set; }

    public int TotalScore { get; set; }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }
}
