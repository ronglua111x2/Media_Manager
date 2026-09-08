using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class TrackedMovieService : ITrackedMovieService
{
    private readonly IDatabaseService _databaseService;
    private readonly ITmdbMovieCatalogService _catalogService;
    private readonly IPosterImageService _posterImageService;
    private readonly IAppLogger _logger;

    public TrackedMovieService(
        IDatabaseService databaseService,
        ITmdbMovieCatalogService catalogService,
        IPosterImageService posterImageService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _catalogService = catalogService;
        _posterImageService = posterImageService;
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
        await CachePosterAsync(details.TmdbId, details.PosterPath, cancellationToken);
        RefreshAvailability(movieId);
        var movie = _databaseService.GetTrackedMovie(movieId) ?? throw new InvalidOperationException("Tracked movie was not saved.");
        _logger.Info($"Tracked movie added: {movie.DisplayTitle}", LogTarget.All);
        return movie;
    }

    public async Task<TrackedMovie> ImportMovieByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        var details = await _catalogService.GetMovieDetailsAsync(tmdbId, cancellationToken);
        var movieId = ImportMovie(details, null);
        await CachePosterAsync(details.TmdbId, details.PosterPath, cancellationToken);
        RefreshAvailability(movieId);
        var movie = _databaseService.GetTrackedMovie(movieId) ?? throw new InvalidOperationException("Tracked movie was not imported.");
        _logger.Info($"Tracked movie imported from existing media: {movie.DisplayTitle}", LogTarget.All);
        return movie;
    }

    public async Task<TrackedMovie> RefreshMovieAsync(TrackedMovie movie, CancellationToken cancellationToken = default)
    {
        var details = await _catalogService.GetMovieDetailsAsync(movie.TmdbId, cancellationToken);
        var movieId = ImportMovie(details, movie);
        await CachePosterAsync(details.TmdbId, details.PosterPath, cancellationToken);
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
        var sourceItems = _databaseService.GetSourceItems();
        RefreshAvailability(sourceItems);
    }

    public void RefreshAvailability(IReadOnlyList<SourceItem> sourceItems)
    {
        foreach (var movie in _databaseService.GetTrackedMovies())
        {
            RefreshAvailabilityCore(movie, sourceItems);
        }
    }

    public void RefreshAvailability(long movieId)
    {
        var movie = _databaseService.GetTrackedMovie(movieId);
        if (movie is null)
        {
            return;
        }

        RefreshAvailabilityCore(movie, _databaseService.GetSourceItems());
    }

    public void RefreshAvailability(long movieId, IReadOnlyList<SourceItem> sourceItems)
    {
        var movie = _databaseService.GetTrackedMovie(movieId);
        if (movie is null)
        {
            return;
        }

        RefreshAvailabilityCore(movie, sourceItems);
    }

    private void RefreshAvailabilityCore(TrackedMovie movie, IReadOnlyList<SourceItem> sourceItems)
    {
        var providerId = movie.TmdbId.ToString();
        var isAvailable = sourceItems.Any(item =>
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

    public void UpdateTorrentState(long movieId, AddedTorrentResult torrent)
    {
        _databaseService.UpdateTrackedMovieTorrent(
            movieId,
            torrent.Hash,
            torrent.Name,
            QbittorrentTorrentStateNormalizer.Normalize(torrent.State, torrent.IsComplete),
            torrent.Progress);
    }

    public void MarkTorrentRemoved(long movieId, string torrentHash)
    {
        _databaseService.UpdateTrackedMovieTorrent(movieId, string.Empty, string.Empty, string.Empty, 0);
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

    public void UpdateCartOverrides(long movieId, CartRecipeOverrideSet overrides)
    {
        var json = CartRecipeOverrideSet.Serialize(overrides);
        _databaseService.UpdateTrackedMovieCartOverridesJson(movieId, json);
        _logger.Info(
            $"Updated Cart recipe overrides for movie id={movieId}: {json ?? "<none>"}",
            LogTarget.All);
    }

    public void UpdateWatchStatus(long movieId, UserWatchStatus watchStatus)
    {
        _databaseService.UpdateTrackedMovieWatchStatus(movieId, watchStatus);
        _logger.Info($"Updated watch status for movie id={movieId}: {watchStatus}", LogTarget.All);
    }

    public void UpdateRating(long movieId, double? rating, string? thought)
    {
        _databaseService.UpdateTrackedMovieRating(movieId, rating, thought);
        _logger.Info($"Updated rating/thought for movie id={movieId}: rating={rating?.ToString("0.0") ?? "null"}", LogTarget.All);
    }

    public void SetAlternativeTitleExcludedFromSearch(long movieId, string title, bool excluded)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var movie = _databaseService.GetTrackedMovie(movieId)
            ?? throw new InvalidOperationException($"Tracked movie id={movieId} was not found.");
        var excludedTitles = movie.ExcludedFromSearchAlternativeTitles.ToList();
        var normalizedTitle = title.Trim();
        if (excluded)
        {
            if (!excludedTitles.Any(existing => string.Equals(existing, normalizedTitle, StringComparison.OrdinalIgnoreCase)))
            {
                excludedTitles.Add(normalizedTitle);
            }
        }
        else
        {
            excludedTitles.RemoveAll(existing => string.Equals(existing, normalizedTitle, StringComparison.OrdinalIgnoreCase));
        }

        var json = TrackedMovie.SerializeAlternativeTitles(excludedTitles);
        _databaseService.UpdateTrackedMovieExcludedAlternativeTitles(movieId, json);
        _logger.Info(
            $"Alternative title '{normalizedTitle}' {(excluded ? "excluded from" : "included in")} recipe search for movie id={movieId}.",
            LogTarget.All);
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
            AlternativeTitlesJson = TrackedMovie.SerializeAlternativeTitles(details.AlternativeTitles),
            ExcludedAlternativeTitlesJson = existing?.ExcludedAlternativeTitlesJson,
            RecipeId = existing?.RecipeId,
            CartOverridesJson = existing?.CartOverridesJson,
            PreferredQuality = existing?.PreferredQuality ?? "1080p",
            PreferredAudioCodec = existing?.PreferredAudioCodec ?? string.Empty,
            MinimumSeeders = existing?.MinimumSeeders ?? 0,
            Availability = existing?.Availability ?? EpisodeAvailability.Missing,
            TorrentHash = existing?.TorrentHash,
            TorrentName = existing?.TorrentName,
            TorrentState = existing?.TorrentState,
            TorrentProgress = existing?.TorrentProgress ?? 0,
            TorrentUpdatedUtc = existing?.TorrentUpdatedUtc
        });
    }

    private Task CachePosterAsync(int tmdbId, string? posterPath, CancellationToken cancellationToken)
    {
        return _posterImageService.EnsureCachedAsync(MediaKind.Movie, tmdbId, posterPath, cancellationToken);
    }
}
