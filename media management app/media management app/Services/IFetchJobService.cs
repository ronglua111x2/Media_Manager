using media_management_app.Models;

namespace media_management_app.Services;

public interface IFetchJobService
{
    IReadOnlyList<EpisodeFetchCandidate> GetCandidates(long episodeId);

    bool TryGetCandidates(long episodeId, out IReadOnlyList<EpisodeFetchCandidate> candidates);

    IReadOnlyList<EpisodeFetchCandidate> GetMovieCandidates(long movieId);

    bool TryGetMovieCandidates(long movieId, out IReadOnlyList<EpisodeFetchCandidate> candidates);

    bool TryGetPackCandidates(long showId, int seasonNumber, out IReadOnlyList<SeasonPackCandidate> candidates);

    int GetSearchRowCount(long episodeId);

    IReadOnlyDictionary<CandidateRejectReason, int> GetOverrideRejectCounts(long episodeId);

    /// <summary>
    /// "No search source available: …" when the last search of this episode found nothing because no native site
    /// could be asked (paused, breaker open, budget used); null otherwise.
    /// </summary>
    string? GetSearchSourceProblem(long episodeId) => null;

    /// <summary>The same for the last season pack search of <paramref name="showId"/> season <paramref name="seasonNumber"/>.</summary>
    string? GetPackSearchSourceProblem(long showId, int seasonNumber) => null;

    CartCandidateDebugSession? TakeHuntDebugSession();

    Task<IReadOnlyDictionary<long, IReadOnlyList<EpisodeFetchCandidate>>> FetchEpisodeCandidatesAsync(
        long showId,
        IReadOnlyList<long> episodeIds,
        string? recipeId = null,
        Action<long, string>? statusChanged = null,
        CancellationToken cancellationToken = default,
        EpisodeFetchOptions? options = null,
        bool finalizeDebugLog = true);

    Task FetchSeasonPacksAsync(
        long showId,
        IReadOnlyList<int> seasonNumbers,
        CancellationToken cancellationToken = default,
        RecipeExecutionOverrides? overrides = null,
        string? recipeId = null);

}
