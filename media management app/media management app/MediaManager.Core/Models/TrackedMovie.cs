using System.Text.Json;
using media_management_app.Common;

namespace media_management_app.Models;

public sealed class TrackedMovie
{
    public long Id { get; set; }

    public int TmdbId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? ReleaseYear { get; set; }

    public string? Overview { get; set; }

    public string? PosterPath { get; set; }

    public string? AlternativeTitlesJson { get; set; }

    public string? ExcludedAlternativeTitlesJson { get; set; }

    public string? RecipeId { get; set; }

    public string? CartOverridesJson { get; set; }

    public string PreferredQuality { get; set; } = "1080p";

    public string PreferredAudioCodec { get; set; } = string.Empty;

    public int MinimumSeeders { get; set; }

    public EpisodeAvailability Availability { get; set; } = EpisodeAvailability.Missing;

    public UserWatchStatus WatchStatus { get; set; } = UserWatchStatus.None;

    /// <summary>Personal score on a 0–10 scale (one decimal). Null = unset.</summary>
    public double? Rating { get; set; }

    /// <summary>Short personal review/thought (max 250 chars).</summary>
    public string? Thought { get; set; }

    public string WatchStatusLabel => TrackedShow.FormatWatchStatusLabel(WatchStatus);

    public string? TorrentHash { get; set; }

    /// <summary>
    /// Comma-separated, lower-case hashes of the torrents this movie was reset from ("Reset for redownload", migration 013).
    /// Reconcile never adopts them for it again; read with <see cref="WasResetFrom"/> or
    /// <see cref="media_management_app.Services.ResetTorrentHashList"/>.
    /// </summary>
    public string ResetTorrentHashes { get; set; } = string.Empty;

    public bool WasResetFrom(string? hash) => media_management_app.Services.ResetTorrentHashList.Contains(ResetTorrentHashes, hash);

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

    public string DisplayTitle => ReleaseYear is null ? Title : $"{Title} ({ReleaseYear})";

    public IReadOnlyList<string> AlternativeTitles => TrackedShow.ParseAlternativeTitles(AlternativeTitlesJson);

    public IReadOnlyList<string> ExcludedFromSearchAlternativeTitles =>
        TrackedShow.ParseAlternativeTitles(ExcludedAlternativeTitlesJson);

    public IReadOnlyList<string> GetSearchableAlternativeTitles() =>
        TrackedShow.GetSearchableAlternativeTitles(AlternativeTitles, ExcludedFromSearchAlternativeTitles);

    public static string? SerializeAlternativeTitles(IEnumerable<string> titles) =>
        TrackedShow.SerializeAlternativeTitles(titles);
}
