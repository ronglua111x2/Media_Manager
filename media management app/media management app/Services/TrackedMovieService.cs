using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class TrackedMovieService : ITrackedMovieService
{
    private readonly IDatabaseService _databaseService;
    private readonly ITmdbMovieCatalogService _catalogService;
    private readonly IAppLogger _logger;

    public TrackedMovieService(IDatabaseService databaseService, ITmdbMovieCatalogService catalogService, IAppLogger logger)
    {
        _databaseService = databaseService;
        _catalogService = catalogService;
        _logger = logger;
    }

    public Task<IReadOnlyList<TmdbMovieSearchResult>> SearchMoviesAsync(string query, CancellationToken cancellationToken = default)
    {
        return _catalogService.SearchMoviesAsync(query, cancellationToken);
    }

    public async Task<TrackedMovie> AddMovieAsync(TmdbMovieSearchResult result, CancellationToken cancellationToken = default)
    {
        var details = await _catalogService.GetMovieDetailsAsync(result.TmdbId, cancellationToken);
        var movieId = ImportMovie(details, null);
        RefreshAvailability(movieId);
        var movie = _databaseService.GetTrackedMovie(movieId) ?? throw new InvalidOperationException("Tracked movie was not saved.");
        _logger.Info($"Tracked movie added: {movie.DisplayTitle}", LogTarget.All);
        return movie;
    }

    public async Task<TrackedMovie> ImportMovieByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        var details = await _catalogService.GetMovieDetailsAsync(tmdbId, cancellationToken);
        var movieId = ImportMovie(details, null);
        RefreshAvailability(movieId);
        var movie = _databaseService.GetTrackedMovie(movieId) ?? throw new InvalidOperationException("Tracked movie was not imported.");
        _logger.Info($"Tracked movie imported from existing media: {movie.DisplayTitle}", LogTarget.All);
        return movie;
    }

    public async Task<TrackedMovie> RefreshMovieAsync(TrackedMovie movie, CancellationToken cancellationToken = default)
    {
        var details = await _catalogService.GetMovieDetailsAsync(movie.TmdbId, cancellationToken);
        var movieId = ImportMovie(details, movie);
        RefreshAvailability(movieId);
        var refreshed = _databaseService.GetTrackedMovie(movieId) ?? throw new InvalidOperationException("Tracked movie was not refreshed.");
        _logger.Info($"Tracked movie refreshed: {refreshed.DisplayTitle}", LogTarget.All);
        return refreshed;
    }

    public IReadOnlyList<TrackedMovie> GetMovies()
    {
        return _databaseService.GetTrackedMovies();
    }

    public void RefreshAvailability()
    {
        foreach (var movie in _databaseService.GetTrackedMovies())
        {
            RefreshAvailability(movie.Id);
        }
    }

    public void RefreshAvailability(long movieId)
    {
        var movie = _databaseService.GetTrackedMovie(movieId);
        if (movie is null)
        {
            return;
        }

        var providerId = movie.TmdbId.ToString();
        var isAvailable = _databaseService.GetSourceItems().Any(item =>
            item.MediaKind == MediaKind.Movie &&
            item.MatchAccepted &&
            item.State is not ItemState.Deleted and not ItemState.Ignored &&
            string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

        var availability = isAvailable ? EpisodeAvailability.Available : EpisodeAvailability.Missing;
        if (movie.Availability != availability)
        {
            _databaseService.UpdateTrackedMovieAvailability(movie.Id, availability);
        }
    }

    public void UpdateWanted(long movieId, bool isWanted)
    {
        _databaseService.UpdateTrackedMovieWanted(movieId, isWanted);
    }

    public void UpdateTorrentState(long movieId, AddedTorrentResult torrent)
    {
        _databaseService.UpdateTrackedMovieTorrent(
            movieId,
            torrent.Hash,
            torrent.Name,
            torrent.IsComplete ? "Downloaded" : torrent.State,
            torrent.Progress);
    }

    public void MarkTorrentRemoved(long movieId, string torrentHash)
    {
        _databaseService.UpdateTrackedMovieTorrent(movieId, torrentHash, string.Empty, "Removed from qBittorrent", 0);
    }

    public void UpdateSelectedCandidate(long movieId, EpisodeFetchCandidate candidate)
    {
        _databaseService.UpdateTrackedMovieSelectedCandidate(movieId, candidate);
        _logger.Info($"Saved selected candidate for movie id={movieId}: '{candidate.FileName}'", LogTarget.All);
    }

    public void UpdatePreferences(long movieId, string preferredQuality, string preferredAudioCodec, int minimumSeeders)
    {
        _databaseService.UpdateTrackedMoviePreferences(movieId, preferredQuality, preferredAudioCodec, minimumSeeders);
        _logger.Info(
            $"Updated Auto Torrent movie preferences for movie id={movieId}: Quality='{preferredQuality}', Audio='{preferredAudioCodec}', MinimumSeeders={minimumSeeders}",
            LogTarget.All);
    }

    public void UpdateRecipe(long movieId, string? recipeId)
    {
        _databaseService.UpdateTrackedMovieRecipe(movieId, recipeId);
        _logger.Info($"Updated recipe assignment for movie id={movieId}: {recipeId ?? "<default>"}", LogTarget.All);
    }

    private long ImportMovie(TmdbMovieDetails details, TrackedMovie? existing)
    {
        existing ??= _databaseService.GetTrackedMovieByTmdbId(details.TmdbId);
        return _databaseService.UpsertTrackedMovie(new TrackedMovie
        {
            TmdbId = details.TmdbId,
            Title = details.Title,
            ReleaseYear = details.ReleaseYear,
            Overview = details.Overview,
            PosterPath = details.PosterPath,
            RecipeId = existing?.RecipeId,
            PreferredQuality = existing?.PreferredQuality ?? "1080p",
            PreferredAudioCodec = existing?.PreferredAudioCodec ?? string.Empty,
            MinimumSeeders = existing?.MinimumSeeders ?? 0,
            Availability = existing?.Availability ?? EpisodeAvailability.Missing,
            IsWanted = existing?.IsWanted ?? true,
            TorrentHash = existing?.TorrentHash,
            TorrentName = existing?.TorrentName,
            TorrentState = existing?.TorrentState,
            TorrentProgress = existing?.TorrentProgress ?? 0,
            TorrentUpdatedUtc = existing?.TorrentUpdatedUtc
        });
    }
}
