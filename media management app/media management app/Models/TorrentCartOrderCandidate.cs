namespace media_management_app.Models;

public sealed class TorrentCartOrderCandidate
{
    public long Id { get; set; }

    public long OrderId { get; set; }

    public int Rank { get; set; }

    public bool IsSelected { get; set; }

    public bool IsAccepted { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string PluginName { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public string Quality { get; set; } = string.Empty;

    public string AudioCodec { get; set; } = string.Empty;

    public string CoveredSeasons { get; set; } = string.Empty;

    public string ContentProfileJson { get; set; } = string.Empty;

    public int TotalScore { get; set; }
}
