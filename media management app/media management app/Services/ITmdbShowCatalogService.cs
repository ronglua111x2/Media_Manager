using media_management_app.Models;

namespace media_management_app.Services;

public interface ITmdbShowCatalogService
{
    Task<IReadOnlyList<TmdbShowSearchResult>> SearchTvShowsAsync(string query, CancellationToken cancellationToken = default);

    Task<TmdbShowDetails> GetTvShowDetailsAsync(int tmdbId, CancellationToken cancellationToken = default);
}

