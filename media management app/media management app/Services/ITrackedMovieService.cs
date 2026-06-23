using media_management_app.Models;

namespace media_management_app.Services;

public interface ITrackedMovieService
{
    Task<IReadOnlyList<TmdbMovieSearchResult>> SearchMoviesAsync(string query, CancellationToken cancellationToken = default);

    Task<TrackedMovie> AddMovieAsync(TmdbMovieSearchResult result, CancellationToken cancellationToken = default);

    Task<TrackedMovie> ImportMovieByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken = default);

    Task<TrackedMovie> RefreshMovieAsync(TrackedMovie movie, CancellationToken cancellationToken = default);

    IReadOnlyList<TrackedMovie> GetMovies();

    void RefreshAvailability();

    void RefreshAvailability(IReadOnlyList<SourceItem> sourceItems);

    void RefreshAvailability(long movieId);

    void RefreshAvailability(long movieId, IReadOnlyList<SourceItem> sourceItems);

    void UpdateTorrentState(long movieId, AddedTorrentResult torrent);

    void MarkTorrentRemoved(long movieId, string torrentHash);

    void UpdateSelectedCandidate(long movieId, EpisodeFetchCandidate candidate);

    void UpdatePreferences(long movieId, string preferredQuality, string preferredAudioCodec, int minimumSeeders);

    void UpdateRecipe(long movieId, string? recipeId);

    void SetAlternativeTitleExcludedFromSearch(long movieId, string title, bool excluded);
}
