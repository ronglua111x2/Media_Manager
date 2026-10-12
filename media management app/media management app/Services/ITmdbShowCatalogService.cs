using media_management_app.Models;

namespace media_management_app.Services;

public interface ITmdbShowCatalogService
{
    Task<IReadOnlyList<TmdbShowSearchResult>> SearchTvShowsAsync(string query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TmdbShowSearchResult>> SearchTvShowsLightweightAsync(string query, CancellationToken cancellationToken = default);

    Task<TmdbShowDetails> GetTvShowSummaryAsync(int tmdbId, CancellationToken cancellationToken = default);

    Task<TmdbShowDetails> GetTvShowDetailsAsync(int tmdbId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TmdbEpisodeGroupSummary>> GetTvEpisodeGroupsAsync(int tmdbId, CancellationToken cancellationToken = default);

    Task<TmdbShowDetails> GetTvShowDetailsByEpisodeGroupAsync(
        int tmdbId,
        string episodeGroupId,
        CancellationToken cancellationToken = default);
}
