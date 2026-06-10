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
    private readonly IAppLogger _logger;

    public LibraryManagementService(
        IDatabaseService databaseService,
        IAutoTorrentLinkService autoTorrentLinkService,
        ITorrentCartService torrentCartService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _autoTorrentLinkService = autoTorrentLinkService;
        _torrentCartService = torrentCartService;
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
            if (_databaseService.GetTrackedMovie(mediaId) is null)
            {
                result.Messages.Add("Tracked movie was not found.");
                return result;
            }

            MergeLinkResult(result, _autoTorrentLinkService.RemoveMovieLinks(mediaId));
            result.DeletedFetchJobCount = _databaseService.DeleteFetchJobsForMedia(mediaId, MediaKind.Movie);
            _databaseService.DeleteTrackedMovie(mediaId);
            result.DeletedMovieCount = 1;
            _torrentCartService.ClearCart(MediaKind.Movie, mediaId);
        }
        else
        {
            if (_databaseService.GetTrackedShow(mediaId) is null)
            {
                result.Messages.Add("Tracked show was not found.");
                return result;
            }

            MergeLinkResult(result, _autoTorrentLinkService.RemoveShowLinks(mediaId));
            result.DeletedFetchJobCount = _databaseService.DeleteFetchJobsForMedia(mediaId, MediaKind.TvEpisode);
            _databaseService.DeleteTrackedShow(mediaId);
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
