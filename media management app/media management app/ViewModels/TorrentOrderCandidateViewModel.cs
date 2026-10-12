using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed class TorrentOrderCandidateViewModel
{
    public TorrentOrderCandidateViewModel(TorrentCartOrderCandidate candidate)
    {
        Id = candidate.Id;
        OrderId = candidate.OrderId;
        Rank = candidate.Rank;
        IsSelected = candidate.IsSelected;
        IsAccepted = candidate.IsAccepted;
        Name = candidate.Name;
        Url = candidate.Url;
        PluginName = candidate.PluginName;
        FileSize = candidate.FileSize;
        Seeders = candidate.Seeders;
        Leechers = candidate.Leechers;
        Quality = candidate.Quality;
        AudioCodec = candidate.AudioCodec;
        TotalScore = candidate.TotalScore;
        CoveredSeasons = candidate.CoveredSeasons;
        ContentProfileWarning = PackContentProfile.Deserialize(candidate.ContentProfileJson)?.BuildWarningText() ?? string.Empty;
    }

    public long Id { get; }

    public long OrderId { get; }

    public int Rank { get; }

    public bool IsSelected { get; }

    public bool IsAccepted { get; }

    public string Name { get; }

    public string Url { get; }

    public string PluginName { get; }

    public long FileSize { get; }

    public int Seeders { get; }

    public int Leechers { get; }

    public string Quality { get; }

    public string AudioCodec { get; }

    public int TotalScore { get; }

    public string CoveredSeasons { get; }

    public bool IsMultiSeason => ParseCoveredSeasons(CoveredSeasons).Count > 1;

    public string CoveredSeasonsDisplay
    {
        get
        {
            var seasons = ParseCoveredSeasons(CoveredSeasons);
            return seasons.Count == 0
                ? string.Empty
                : string.Join(", ", seasons.Select(season => $"S{season:00}"));
        }
    }

    public string MultiSeasonWarningText => IsMultiSeason
        ? $"Multi-season pack: covers {CoveredSeasonsDisplay}"
        : string.Empty;

    public string ContentProfileWarning { get; }

    public bool HasContentProfileWarning => !string.IsNullOrWhiteSpace(ContentProfileWarning);

    public string FileSizeDisplay => FormatSize(FileSize);

    public string RankLabel => $"#{Rank}";

    public string QualityLabel => QualityDisplay;

    public string StatsLine => $"{FileSizeDisplay} · {Seeders} seeders · score {TotalScore}";

    public string DisplayText => IsMultiSeason
        ? $"#{Rank} {Name} | {QualityDisplay} | {FileSizeDisplay} | {Seeders} seeders | score {TotalScore} | covers {CoveredSeasonsDisplay}"
        : $"#{Rank} {Name} | {QualityDisplay} | {FileSizeDisplay} | {Seeders} seeders | score {TotalScore}";

    private string QualityDisplay => string.IsNullOrWhiteSpace(Quality) ? "unknown" : Quality;

    private static IReadOnlyList<int> ParseCoveredSeasons(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.TryParse(item, out var season) ? season : 0)
            .Where(season => season > 0)
            .Distinct()
            .Order()
            .ToList();
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
