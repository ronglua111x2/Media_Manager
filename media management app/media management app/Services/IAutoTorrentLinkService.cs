using media_management_app.Common;
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

    /// <summary>
    /// Moves the links of a show's episodes from their old number to their new one (key = old number, value = new):
    /// library hardlinks are renamed, records renumbered and the Jellyfin symlinks follow (audit L3-09). All or
    /// nothing: when a link cannot move, <see cref="EpisodeLinkMoveResult.BlockedKeys"/> or
    /// <see cref="EpisodeLinkMoveResult.Failed"/> says so and nothing was changed.
    /// </summary>
    EpisodeLinkMoveResult MoveEpisodeLinks(
        long showId,
        IReadOnlyDictionary<(int SeasonNumber, int EpisodeNumber), (int SeasonNumber, int EpisodeNumber)> moves);

    AutoTorrentLinkResult RemoveMovieLinks(long movieId);

    /// <summary>
    /// Checks, without changing anything, that every library link of a tracked show or movie can be removed now.
    /// ErrorCount and Messages name the links that cannot (open in a player, drive offline, outside the library).
    /// </summary>
    AutoTorrentLinkResult CheckMediaLinksRemovable(MediaKind mediaKind, long mediaId);

    AutoTorrentLinkResult RemoveOrphanPackSpecialLink(long sourceItemId);

    AutoTorrentLinkResult RefreshLinkStatus(long? showId = null, long? movieId = null);

    AutoTorrentLinkResult ResetEpisodeForRedownload(long showId, int seasonNumber, int episodeNumber);

    Task<AutoTorrentLinkResult> ResetSeasonPackForRedownloadAsync(
        long showId,
        int ownerSeasonNumber,
        CancellationToken cancellationToken = default);

    AutoTorrentLinkResult ResetMovieForRedownload(long movieId);
}
