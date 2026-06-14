using media_management_app.Common;
using media_management_app.ViewModels;

namespace media_management_app.Services;

public interface IMediaCardCatalogService
{
    IReadOnlyList<LibraryMediaCardViewModel> LoadCards();
}

public sealed class MediaCardCatalogService : IMediaCardCatalogService
{
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly ITorrentCartService _torrentCartService;

    public MediaCardCatalogService(
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        ITorrentCartService torrentCartService)
    {
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _torrentCartService = torrentCartService;
    }

    public IReadOnlyList<LibraryMediaCardViewModel> LoadCards()
    {
        var shows = _trackedShowService.GetShows().Select(show => new LibraryMediaCardViewModel
        {
            Id = show.Id,
            TmdbId = show.TmdbId,
            Title = show.Title,
            MediaKind = MediaKind.TvEpisode,
            Year = show.FirstAirYear,
            CreatedUtc = show.CreatedUtc,
            AvailableCount = show.AvailableEpisodes,
            TotalCount = show.TotalEpisodes,
            Overview = show.Overview,
            PosterPath = show.PosterPath,
            SeriesStatusLabel = show.SeriesStatusLabel,
            OrderCount = _torrentCartService.GetOrderCount(MediaKind.TvEpisode, show.Id)
        });
        var movies = _trackedMovieService.GetMovies().Select(movie => new LibraryMediaCardViewModel
        {
            Id = movie.Id,
            TmdbId = movie.TmdbId,
            Title = movie.Title,
            MediaKind = MediaKind.Movie,
            Year = movie.ReleaseYear,
            CreatedUtc = movie.CreatedUtc,
            AvailableCount = movie.Availability == EpisodeAvailability.Available ? 1 : 0,
            TotalCount = 1,
            Overview = movie.Overview,
            PosterPath = movie.PosterPath,
            OrderCount = _torrentCartService.GetOrderCount(MediaKind.Movie, movie.Id)
        });

        return shows.Concat(movies).ToList();
    }
}
