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

    bool HasActiveJobs();

    Task<FetchJob> EnqueueShowFetchAsync(long showId, CancellationToken cancellationToken = default);

    Task<FetchJob> EnqueueMovieFetchAsync(long movieId, CancellationToken cancellationToken = default);

    void CancelJob(long jobId);

    void CancelActiveJobs();

    Task<FetchJob> RetryJobAsync(long jobId, CancellationToken cancellationToken = default);

    void DeleteJob(long jobId);
}
