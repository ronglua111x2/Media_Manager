using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface IMediaMetadataSyncService
{
    Task<ShowMetadataSyncResult> RefreshShowAsync(long showId, CancellationToken cancellationToken = default);

    Task<MovieMetadataSyncResult> RefreshMovieAsync(long movieId, CancellationToken cancellationToken = default);

    Task<OngoingShowsSyncResult> RefreshOngoingShowsAsync(CancellationToken cancellationToken = default);

    Task<LibraryMetadataSyncResult> RefreshAllLibraryFromTmdbAsync(CancellationToken cancellationToken = default);
}

public sealed class MediaMetadataSyncService : IMediaMetadataSyncService
{
    private readonly IDatabaseService _databaseService;
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly IAppLogger _logger;

    public MediaMetadataSyncService(
        IDatabaseService databaseService,
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _logger = logger;
    }

    public async Task<ShowMetadataSyncResult> RefreshShowAsync(long showId, CancellationToken cancellationToken = default)
    {
        var show = _databaseService.GetTrackedShow(showId);
        if (show is null)
        {
            return new ShowMetadataSyncResult
            {
                ShowId = showId,
                Success = false,
                ErrorMessage = "Show was not found."
            };
        }

        var episodeCountBefore = _databaseService.GetTrackedEpisodes(showId).Count;
        try
        {
            await _trackedShowService.RefreshShowAsync(show, cancellationToken);
            var episodeCountAfter = _databaseService.GetTrackedEpisodes(showId).Count;
            var newEpisodesAdded = Math.Max(0, episodeCountAfter - episodeCountBefore);
            _logger.Info(
                $"TMDB refresh succeeded for show id={showId}: {show.Title}, new episodes={newEpisodesAdded}",
                LogTarget.All);

            return new ShowMetadataSyncResult
            {
                ShowId = showId,
                Title = show.Title,
                Success = true,
                NewEpisodesAdded = newEpisodesAdded
            };
        }
        catch (Exception ex)
        {
            _logger.Warning($"TMDB refresh failed for show id={showId}: {show.Title}. {ex.Message}", LogTarget.All);
            return new ShowMetadataSyncResult
            {
                ShowId = showId,
                Title = show.Title,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<MovieMetadataSyncResult> RefreshMovieAsync(long movieId, CancellationToken cancellationToken = default)
    {
        var movie = _databaseService.GetTrackedMovie(movieId);
        if (movie is null)
        {
            return new MovieMetadataSyncResult
            {
                MovieId = movieId,
                Success = false,
                ErrorMessage = "Movie was not found."
            };
        }

        return await RefreshMovieCoreAsync(movie, cancellationToken);
    }

    public async Task<OngoingShowsSyncResult> RefreshOngoingShowsAsync(CancellationToken cancellationToken = default)
    {
        var ongoingShows = _databaseService.GetTrackedShowsBySeriesStatus(ShowSeriesStatus.Ongoing);
        var showResults = new List<ShowMetadataSyncResult>();

        foreach (var show in ongoingShows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            showResults.Add(await RefreshShowAsync(show.Id, cancellationToken));
        }

        return BuildOngoingShowsSyncResult(showResults);
    }

    public async Task<LibraryMetadataSyncResult> RefreshAllLibraryFromTmdbAsync(CancellationToken cancellationToken = default)
    {
        var showResults = new List<ShowMetadataSyncResult>();
        foreach (var show in _trackedShowService.GetShows())
        {
            cancellationToken.ThrowIfCancellationRequested();
            showResults.Add(await RefreshShowAsync(show.Id, cancellationToken));
        }

        var movieResults = new List<MovieMetadataSyncResult>();
        foreach (var movie in _trackedMovieService.GetMovies())
        {
            cancellationToken.ThrowIfCancellationRequested();
            movieResults.Add(await RefreshMovieCoreAsync(movie, cancellationToken));
        }

        return BuildLibraryMetadataSyncResult(showResults, movieResults);
    }

    private async Task<MovieMetadataSyncResult> RefreshMovieCoreAsync(TrackedMovie movie, CancellationToken cancellationToken)
    {
        try
        {
            await _trackedMovieService.RefreshMovieAsync(movie, cancellationToken);
            _logger.Info($"TMDB refresh succeeded for movie id={movie.Id}: {movie.Title}", LogTarget.All);
            return new MovieMetadataSyncResult
            {
                MovieId = movie.Id,
                Title = movie.Title,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.Warning($"TMDB refresh failed for movie id={movie.Id}: {movie.Title}. {ex.Message}", LogTarget.All);
            return new MovieMetadataSyncResult
            {
                MovieId = movie.Id,
                Title = movie.Title,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static OngoingShowsSyncResult BuildOngoingShowsSyncResult(IReadOnlyList<ShowMetadataSyncResult> showResults)
    {
        var succeeded = showResults.Count(result => result.Success);
        var failed = showResults.Count - succeeded;
        return new OngoingShowsSyncResult
        {
            ShowsChecked = showResults.Count,
            ShowsSucceeded = succeeded,
            ShowsFailed = failed,
            TotalNewEpisodesAdded = showResults.Sum(result => result.NewEpisodesAdded),
            ShowResults = showResults
        };
    }

    private static LibraryMetadataSyncResult BuildLibraryMetadataSyncResult(
        IReadOnlyList<ShowMetadataSyncResult> showResults,
        IReadOnlyList<MovieMetadataSyncResult> movieResults)
    {
        var showsRefreshed = showResults.Count(result => result.Success);
        var showsFailed = showResults.Count - showsRefreshed;
        var moviesRefreshed = movieResults.Count(result => result.Success);
        var moviesFailed = movieResults.Count - moviesRefreshed;

        return new LibraryMetadataSyncResult
        {
            ShowsRefreshed = showsRefreshed,
            ShowsFailed = showsFailed,
            MoviesRefreshed = moviesRefreshed,
            MoviesFailed = moviesFailed,
            TotalNewEpisodesAdded = showResults.Sum(result => result.NewEpisodesAdded),
            ShowResults = showResults,
            MovieResults = movieResults
        };
    }
}
