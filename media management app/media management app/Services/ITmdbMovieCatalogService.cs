using media_management_app.Models;

namespace media_management_app.Services;

public interface ITmdbMovieCatalogService
{
    Task<IReadOnlyList<TmdbMovieSearchResult>> SearchMoviesAsync(string query, CancellationToken cancellationToken = default);

    Task<TmdbMovieDetails> GetMovieDetailsAsync(int tmdbId, CancellationToken cancellationToken = default);
}
