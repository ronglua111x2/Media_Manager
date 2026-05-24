using media_management_app.Common;

namespace media_management_app.Models;

public sealed class TrackedEpisode
{
    public long Id { get; set; }

    public long ShowId { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime? AirDate { get; set; }

    public string AirDateDisplay => AirDate?.ToString("yyyy-MM-dd") ?? string.Empty;

    public EpisodeAvailability Availability { get; set; } = EpisodeAvailability.Missing;

    public bool IsWanted { get; set; }

    public string? TorrentHash { get; set; }

    public string? TorrentName { get; set; }

    public string? TorrentState { get; set; }

    public double TorrentProgress { get; set; }

    public DateTime? TorrentUpdatedUtc { get; set; }

    public string? SelectedCandidateName { get; set; }

    public string? SelectedCandidateUrl { get; set; }

    public string? SelectedCandidatePlugin { get; set; }

    public long SelectedCandidateFileSize { get; set; }

    public int SelectedCandidateSeeders { get; set; }

    public string? SelectedCandidateQuality { get; set; }

    public string? SelectedCandidateAudioCodec { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
