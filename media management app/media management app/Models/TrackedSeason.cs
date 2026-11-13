namespace media_management_app.Models;

using media_management_app.Common;

public sealed class TrackedSeason
{
    public long Id { get; set; }

    public long ShowId { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeCount { get; set; }

    public string? DownloadFolder { get; set; }

    public SeasonManagementMode ManagementMode { get; set; } = SeasonManagementMode.Episode;

    public string? SelectedPackCandidateName { get; set; }

    public string? SelectedPackCandidateUrl { get; set; }

    public string? SelectedPackCandidatePlugin { get; set; }

    public long SelectedPackCandidateFileSize { get; set; }

    public int SelectedPackCandidateSeeders { get; set; }

    public string? SelectedPackCandidateQuality { get; set; }

    public string? SelectedPackCandidateAudioCodec { get; set; }

    public string? SelectedPackCoveredSeasons { get; set; }

    public string? SelectedPackContentProfile { get; set; }

    public int? SelectedPackOwnerSeasonNumber { get; set; }

    public string? PackTorrentHash { get; set; }

    public string? PackTorrentName { get; set; }

    public string? PackTorrentState { get; set; }

    public double PackTorrentProgress { get; set; }

    public string? LastPackLinkTorrentHash { get; set; }

    public DateTime? LastPackLinkUtc { get; set; }

    /// <summary>
    /// Comma-separated, lower-case hashes of the torrents this season (the pack it was owned by or covered by) was reset from ("Reset for redownload", migration 013).
    /// Reconcile never adopts them for it again; read with <see cref="WasResetFrom"/> or
    /// <see cref="media_management_app.Services.ResetTorrentHashList"/>.
    /// </summary>
    public string ResetTorrentHashes { get; set; } = string.Empty;

    public bool WasResetFrom(string? hash) => media_management_app.Services.ResetTorrentHashList.Contains(ResetTorrentHashes, hash);

    public bool IsHidden { get; set; }
}
