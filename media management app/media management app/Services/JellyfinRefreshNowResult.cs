using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public enum JellyfinRefreshNowOutcome
{
    /// <summary>Jellyfin accepted the path notify.</summary>
    Sent,

    /// <summary>No existing paths to send; no HTTP call made.</summary>
    NothingToSend,

    /// <summary>Jellyfin refresh is disabled or missing URL / API key; no HTTP call made.</summary>
    NotConfigured,

    /// <summary>The notify failed (paths may be kept queued for retry).</summary>
    Failed
}

/// <summary>Result of <see cref="IJellyfinLibraryRefreshService.RefreshNowAsync"/>.</summary>
public sealed record JellyfinRefreshNowResult(
    JellyfinRefreshNowOutcome Outcome,
    int SentCount,
    int SkippedCount,
    string? Error = null)
{
    public bool Succeeded => Outcome == JellyfinRefreshNowOutcome.Sent;

    /// <summary>Short status-line text, without paths (paths contain titles).</summary>
    public string ToStatusText()
    {
        var skipped = SkippedCount > 0 ? $" ({SkippedCount} missing on disk, skipped)" : string.Empty;
        return Outcome switch
        {
            JellyfinRefreshNowOutcome.Sent => $"Jellyfin refresh sent for {SentCount} path(s){skipped}.",
            JellyfinRefreshNowOutcome.NotConfigured => "Jellyfin refresh is not configured (Settings → Auto-Track → Jellyfin).",
            JellyfinRefreshNowOutcome.NothingToSend => SkippedCount > 0
                ? $"Jellyfin refresh: no link file on disk ({SkippedCount} missing)."
                : "Jellyfin refresh: nothing linked to send.",
            _ => $"Jellyfin refresh failed: {Error ?? "unknown error"}{skipped}."
        };
    }
}

/// <summary>Collects the Jellyfin-visible symlink paths of a library item from source items.</summary>
public static class JellyfinRefreshTargets
{
    public static IReadOnlyList<string> ForMovie(int tmdbId, IEnumerable<SourceItem> sourceItems) =>
        Select(sourceItems, MediaKind.Movie, tmdbId, item => true);

    /// <summary>Show, one season (<paramref name="seasonNumber"/>), or one episode (both numbers).</summary>
    public static IReadOnlyList<string> ForShow(
        int tmdbId,
        IEnumerable<SourceItem> sourceItems,
        int? seasonNumber = null,
        int? episodeNumber = null) =>
        Select(sourceItems, MediaKind.TvEpisode, tmdbId, item =>
        {
            var season = item.MappedSeasonNumber ?? item.SeasonNumber;
            var episode = item.MappedEpisodeNumber ?? item.EpisodeNumber;
            return (seasonNumber is null || season == seasonNumber) &&
                   (episodeNumber is null || episode == episodeNumber);
        });

    private static IReadOnlyList<string> Select(
        IEnumerable<SourceItem> sourceItems,
        MediaKind kind,
        int tmdbId,
        Func<SourceItem, bool> match)
    {
        var providerId = tmdbId.ToString();
        return sourceItems
            .Where(item =>
                item.MediaKind == kind &&
                item.MatchAccepted &&
                item.State == ItemState.Linked &&
                !string.IsNullOrWhiteSpace(item.SymlinkPath) &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                match(item))
            .Select(item => item.SymlinkPath!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
