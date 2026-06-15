using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface ILibraryManagementService
{
    LibraryDeleteResult DeleteEntireLibrary();

    LibraryDeleteResult DeleteShow(long showId);

    LibraryDeleteResult DeleteMovie(long movieId);

    LibraryDeleteResult DeleteSelectedMedia(MediaKind mediaKind, long mediaId);
}

public sealed class LibraryManagementService : ILibraryManagementService
{
    private readonly IDatabaseService _databaseService;
    private readonly IAutoTorrentLinkService _autoTorrentLinkService;
    private readonly ITorrentCartService _torrentCartService;
    private readonly IPosterImageService _posterImageService;
    private readonly IAppLogger _logger;

    public LibraryManagementService(
        IDatabaseService databaseService,
        IAutoTorrentLinkService autoTorrentLinkService,
        ITorrentCartService torrentCartService,
        IPosterImageService posterImageService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _autoTorrentLinkService = autoTorrentLinkService;
        _torrentCartService = torrentCartService;
        _posterImageService = posterImageService;
        _logger = logger;
    }

    public LibraryDeleteResult DeleteEntireLibrary()
    {
        var result = new LibraryDeleteResult();
        foreach (var show in _databaseService.GetTrackedShows())
        {
            MergeLinkResult(result, _autoTorrentLinkService.RemoveShowLinks(show.Id));
        }

        foreach (var movie in _databaseService.GetTrackedMovies())
        {
            MergeLinkResult(result, _autoTorrentLinkService.RemoveMovieLinks(movie.Id));
        }

        result.DeletedFetchJobCount = _databaseService.DeleteAllFetchJobs();
        result.DeletedShowCount = _databaseService.DeleteAllTrackedShows();
        result.DeletedMovieCount = _databaseService.DeleteAllTrackedMovies();
        _torrentCartService.ClearAllCarts();
        _posterImageService.DeleteAllCached();

        _logger.Warning(
            $"Deleted entire library. {result.Summary} FetchJobs={result.DeletedFetchJobCount}.",
            LogTarget.All);
        return result;
    }

    public LibraryDeleteResult DeleteShow(long showId)
    {
        return DeleteSelectedMedia(MediaKind.TvEpisode, showId);
    }

    public LibraryDeleteResult DeleteMovie(long movieId)
    {
        return DeleteSelectedMedia(MediaKind.Movie, movieId);
    }

    public LibraryDeleteResult DeleteSelectedMedia(MediaKind mediaKind, long mediaId)
    {
        var result = new LibraryDeleteResult();
        if (mediaKind == MediaKind.Movie)
        {
            var movie = _databaseService.GetTrackedMovie(mediaId);
            if (movie is null)
            {
                result.Messages.Add("Tracked movie was not found.");
                return result;
            }

            MergeLinkResult(result, _autoTorrentLinkService.RemoveMovieLinks(mediaId));
            result.DeletedFetchJobCount = _databaseService.DeleteFetchJobsForMedia(mediaId, MediaKind.Movie);
            _databaseService.DeleteTrackedMovie(mediaId);
            _posterImageService.DeleteCached(MediaKind.Movie, movie.TmdbId);
            result.DeletedMovieCount = 1;
            _torrentCartService.ClearCart(MediaKind.Movie, mediaId);
        }
        else
        {
            var show = _databaseService.GetTrackedShow(mediaId);
            if (show is null)
            {
                result.Messages.Add("Tracked show was not found.");
                return result;
            }

            MergeLinkResult(result, _autoTorrentLinkService.RemoveShowLinks(mediaId));
            result.DeletedFetchJobCount = _databaseService.DeleteFetchJobsForMedia(mediaId, MediaKind.TvEpisode);
            _databaseService.DeleteTrackedShow(mediaId);
            _posterImageService.DeleteCached(MediaKind.TvEpisode, show.TmdbId);
            result.DeletedShowCount = 1;
            _torrentCartService.ClearCart(MediaKind.TvEpisode, mediaId);
        }

        _logger.Info($"Deleted tracked media {mediaKind} id={mediaId}. {result.Summary}", LogTarget.All);
        return result;
    }

    private static void MergeLinkResult(LibraryDeleteResult result, AutoTorrentLinkResult linkResult)
    {
        result.RemovedHardlinkCount += linkResult.RemovedCount;
        result.HardlinkErrorCount += linkResult.ErrorCount;
        result.Messages.AddRange(linkResult.Messages);
    }
}
