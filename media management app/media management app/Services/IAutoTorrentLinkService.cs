using media_management_app.Models;

namespace media_management_app.Services;

public interface IAutoTorrentLinkService
{
    Task<AutoTorrentLinkResult> LinkEpisodeAsync(long showId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken = default);

    Task<AutoTorrentLinkResult> LinkShowAsync(long showId, CancellationToken cancellationToken = default);

    Task<AutoTorrentLinkResult> LinkSeasonPackAsync(long showId, int ownerSeasonNumber, CancellationToken cancellationToken = default);

    Task<AutoTorrentLinkResult> LinkMovieAsync(long movieId, CancellationToken cancellationToken = default);

    AutoTorrentLinkResult RemoveEpisodeLinks(long showId, int seasonNumber, int episodeNumber);

    AutoTorrentLinkResult RemoveSeasonPackLinks(long showId, int ownerSeasonNumber);

    AutoTorrentLinkResult RemoveShowLinks(long showId);

    AutoTorrentLinkResult RemoveMovieLinks(long movieId);

    AutoTorrentLinkResult RefreshLinkStatus(long? showId = null, long? movieId = null);
}
