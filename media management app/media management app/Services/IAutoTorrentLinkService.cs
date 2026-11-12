using media_management_app.Models;

namespace media_management_app.Services;

public interface IAutoTorrentLinkService
{
    Task<AutoTorrentLinkResult> LinkEpisodeAsync(long showId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken = default);

    Task<SeasonPackLinkPreview> PrepareSeasonPackLinkAsync(
        long showId,
        int ownerSeasonNumber,
        IProgress<PackLinkProgressUpdate>? progress = null,
        bool useGeminiForSpecials = true,
        bool bypassCache = false,
        CancellationToken cancellationToken = default);

    Task<AutoTorrentLinkResult> ApplySeasonPackLinkAsync(
        SeasonPackLinkPreview preview,
        IProgress<PackLinkProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    Task<AutoTorrentLinkResult> LinkMovieAsync(long movieId, CancellationToken cancellationToken = default);

    AutoTorrentLinkResult RemoveEpisodeLinks(long showId, int seasonNumber, int episodeNumber);

    AutoTorrentLinkResult RemoveSeasonPackLinks(long showId, int ownerSeasonNumber);

    AutoTorrentLinkResult RemoveShowLinks(long showId);

    /// <summary>
    /// Removes every TV library link of a show and then its TV link records, so a new episode
    /// numbering starts clean. Records are kept when any link could not be removed.
    /// </summary>
    AutoTorrentLinkResult ClearShowLinksForRenumbering(long showId);

    AutoTorrentLinkResult RemoveMovieLinks(long movieId);

    AutoTorrentLinkResult RemoveOrphanPackSpecialLink(long sourceItemId);

    AutoTorrentLinkResult RefreshLinkStatus(long? showId = null, long? movieId = null);

    AutoTorrentLinkResult ResetEpisodeForRedownload(long showId, int seasonNumber, int episodeNumber);

    Task<AutoTorrentLinkResult> ResetSeasonPackForRedownloadAsync(
        long showId,
        int ownerSeasonNumber,
        CancellationToken cancellationToken = default);

    AutoTorrentLinkResult ResetMovieForRedownload(long movieId);
}
