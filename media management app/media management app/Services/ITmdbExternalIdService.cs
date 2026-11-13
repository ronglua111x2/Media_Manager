namespace media_management_app.Services;

/// <summary>TMDB external ids (native-search P3: EZTV's official API searches by IMDb id).</summary>
public interface ITmdbExternalIdService
{
    Task<string?> GetImdbIdAsync(bool isMovie, int tmdbId, CancellationToken cancellationToken = default);
}
