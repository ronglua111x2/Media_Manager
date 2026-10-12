using media_management_app.Models;

namespace media_management_app.Services;

public interface IJellyfinClient
{
    Task<string> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task ReportMediaUpdatedAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JellyfinScheduledTaskInfo>> GetScheduledTasksAsync(CancellationToken cancellationToken = default);

    Task<string?> FindSeriesItemIdAsync(
        int showTmdbId,
        CancellationToken cancellationToken = default);

    Task<string?> FindMovieItemIdAsync(
        int movieTmdbId,
        CancellationToken cancellationToken = default);

    Task<string?> FindEpisodeItemIdAsync(
        int showTmdbId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken = default);
}
