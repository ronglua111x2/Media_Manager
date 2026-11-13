using media_management_app.Common;

namespace media_management_app.Models;

public sealed class TrackedEpisode
{
    public long Id { get; set; }

    public long ShowId { get; set; }

    /// <summary>
    /// TMDB episode id of the episode this row (and its link and download) belongs to. When TMDB renumbers, the row
    /// follows this id to its new number (audit L3-09). Null until the first refresh that reads it.
    /// </summary>
    public int? TmdbEpisodeId { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    /// <summary>
    /// Season and episode the assigned download is named with, when TMDB renumbered the episode after it was chosen;
    /// null when it is named with <see cref="SeasonNumber"/>/<see cref="EpisodeNumber"/> (audit L3-09).
    /// </summary>
    public int? DownloadSeasonNumber { get; set; }

    public int? DownloadEpisodeNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime? AirDate { get; set; }

    public string AirDateDisplay => AirDate?.ToString("yyyy-MM-dd") ?? string.Empty;

    public string? Overview { get; set; }

    public double? VoteAverage { get; set; }

    public double? UserRating { get; set; }

    public string? Thought { get; set; }

    public string? StillPath { get; set; }

    public EpisodeAvailability Availability { get; set; } = EpisodeAvailability.Missing;

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

    /// <summary>Set when TMDB stopped listing this episode while something still refers to it.</summary>
    public DateTime? RemovedUpstreamUtc { get; set; }

    public bool IsRemovedUpstream => RemovedUpstreamUtc is not null;

    /// <summary>
    /// Comma-separated, lower-case hashes of the torrents this episode was reset from ("Reset for redownload", migration 013).
    /// Reconcile never adopts them for it again; read with <see cref="WasResetFrom"/> or
    /// <see cref="media_management_app.Services.ResetTorrentHashList"/>.
    /// </summary>
    public string ResetTorrentHashes { get; set; } = string.Empty;

    public bool WasResetFrom(string? hash) => media_management_app.Services.ResetTorrentHashList.Contains(ResetTorrentHashes, hash);

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
