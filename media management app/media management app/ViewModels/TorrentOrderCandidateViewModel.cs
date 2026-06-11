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

    public string FileSizeDisplay => FormatSize(FileSize);

    public string DisplayText => $"#{Rank} {Name} | {QualityDisplay} | {FileSizeDisplay} | {Seeders} seeders | score {TotalScore}";

    private string QualityDisplay => string.IsNullOrWhiteSpace(Quality) ? "unknown" : Quality;

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
