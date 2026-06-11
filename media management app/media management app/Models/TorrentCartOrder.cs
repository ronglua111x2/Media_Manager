using media_management_app.Common;

namespace media_management_app.Models;

public sealed class TorrentCartOrder
{
    public long Id { get; set; }

    public MediaKind TargetKind { get; set; }

    public long MediaId { get; set; }

    public long? EpisodeId { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public TorrentOrderStatus Status { get; set; } = TorrentOrderStatus.Draft;

    public string StatusDetail { get; set; } = string.Empty;

    public string SelectedCandidateName { get; set; } = string.Empty;

    public string SelectedCandidateUrl { get; set; } = string.Empty;

    public string SelectedCandidatePlugin { get; set; } = string.Empty;

    public long SelectedCandidateFileSize { get; set; }

    public int SelectedCandidateSeeders { get; set; }

    public int SelectedCandidateLeechers { get; set; }

    public string SelectedCandidateQuality { get; set; } = string.Empty;

    public string SelectedCandidateAudioCodec { get; set; } = string.Empty;

    public string SelectedCandidateCoveredSeasons { get; set; } = string.Empty;

    public int SelectedCandidateTotalScore { get; set; }

    public string TorrentHash { get; set; } = string.Empty;

    public string TorrentName { get; set; } = string.Empty;

    public string TorrentState { get; set; } = string.Empty;

    public double TorrentProgress { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool HasSelectedCandidate => !string.IsNullOrWhiteSpace(SelectedCandidateUrl);
}
