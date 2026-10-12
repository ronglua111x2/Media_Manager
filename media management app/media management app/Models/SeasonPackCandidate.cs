namespace media_management_app.Models;

public sealed class SeasonPackCandidate
{
    public long ShowId { get; set; }

    public int OwnerSeasonNumber { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string FileUrl { get; set; } = string.Empty;

    public string PluginName { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public string QualityLabel { get; set; } = string.Empty;

    public string AudioCodecLabel { get; set; } = string.Empty;

    public IReadOnlyList<int> CoveredSeasons { get; set; } = [];

    public PackContentProfile? ContentProfile { get; set; }

    public int TotalScore { get; set; }

    public string Warning { get; set; } = string.Empty;

    public bool IsMultiSeason => CoveredSeasons.Count > 1;

    public string CoveredSeasonsDisplay => CoveredSeasons.Count == 0
        ? string.Empty
        : string.Join(", ", CoveredSeasons.Select(season => $"S{season:00}"));

    public string FileSizeDisplay => FormatSize(FileSize);

    public string DisplayName
    {
        get
        {
            var title = FileName.Length <= 88 ? FileName : $"{FileName[..85]}...";
            var quality = string.IsNullOrWhiteSpace(QualityLabel) ? "unknown" : QualityLabel;
            var seasonTags = ContentProfile?.TagsDisplay ?? CoveredSeasonsDisplay;
            return $"{title} | {seasonTags} | {quality} | {FileSizeDisplay} | {Seeders} seeders";
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
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
