using media_management_app.Models;

namespace media_management_app.Services;

public interface IFetchJobService
{
    event EventHandler? JobsChanged;

    event EventHandler? CandidatesChanged;

    IReadOnlyList<FetchJob> GetJobs();

    IReadOnlyList<EpisodeFetchCandidate> GetCandidates(long episodeId);

    bool TryGetCandidates(long episodeId, out IReadOnlyList<EpisodeFetchCandidate> candidates);

    IReadOnlyList<EpisodeFetchCandidate> GetMovieCandidates(long movieId);

    bool TryGetMovieCandidates(long movieId, out IReadOnlyList<EpisodeFetchCandidate> candidates);

    bool TryGetPackCandidates(long showId, int seasonNumber, out IReadOnlyList<SeasonPackCandidate> candidates);

    bool HasActiveJobs();

    Task<FetchJob> EnqueueShowFetchAsync(long showId, CancellationToken cancellationToken = default);

    Task<FetchJob> EnqueueMovieFetchAsync(long movieId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<long, IReadOnlyList<EpisodeFetchCandidate>>> FetchEpisodeCandidatesAsync(
        long showId,
        IReadOnlyList<long> episodeIds,
        string? recipeId = null,
        Action<long, string>? statusChanged = null,
        CancellationToken cancellationToken = default);

    Task FetchSeasonPacksAsync(long showId, IReadOnlyList<int> seasonNumbers, CancellationToken cancellationToken = default, int? maxCandidatesOverride = null);

    void CancelJob(long jobId);

    void CancelActiveJobs();

    Task<FetchJob> RetryJobAsync(long jobId, CancellationToken cancellationToken = default);

    void DeleteJob(long jobId);
}
