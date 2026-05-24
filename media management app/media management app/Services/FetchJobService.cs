using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class FetchJobService : IFetchJobService
{
    private const int SearchCapacityRetryCount = 3;
    private const int SearchCapacityRetryDelayMilliseconds = 2000;

    private readonly IDatabaseService _databaseService;
    private readonly ISettingsService _settingsService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _queueSignal = new(1, 1);
    private readonly Dictionary<long, IReadOnlyList<EpisodeFetchCandidate>> _candidatesByEpisodeId = [];
    private readonly Dictionary<long, IReadOnlyList<EpisodeFetchCandidate>> _candidatesByMovieId = [];
    private readonly object _gate = new();
    private CancellationTokenSource? _activeJobCancellation;
    private bool _queueLoopRunning;

    public FetchJobService(
        IDatabaseService databaseService,
        ISettingsService settingsService,
        IQbittorrentClient qbittorrentClient,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _settingsService = settingsService;
        _qbittorrentClient = qbittorrentClient;
        _logger = logger;
        RecoverPersistedJobs();
    }

    public event EventHandler? JobsChanged;

    public event EventHandler? CandidatesChanged;

    public IReadOnlyList<FetchJob> GetJobs()
    {
        return _databaseService.GetFetchJobs();
    }

    public IReadOnlyList<EpisodeFetchCandidate> GetCandidates(long episodeId)
    {
        lock (_gate)
        {
            return _candidatesByEpisodeId.TryGetValue(episodeId, out var candidates) ? candidates : [];
        }
    }

    public bool TryGetCandidates(long episodeId, out IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        lock (_gate)
        {
            if (_candidatesByEpisodeId.TryGetValue(episodeId, out var storedCandidates))
            {
                candidates = storedCandidates;
                return true;
            }

            candidates = [];
            return false;
        }
    }

    public IReadOnlyList<EpisodeFetchCandidate> GetMovieCandidates(long movieId)
    {
        lock (_gate)
        {
            return _candidatesByMovieId.TryGetValue(movieId, out var candidates) ? candidates : [];
        }
    }

    public bool TryGetMovieCandidates(long movieId, out IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        lock (_gate)
        {
            if (_candidatesByMovieId.TryGetValue(movieId, out var storedCandidates))
            {
                candidates = storedCandidates;
                return true;
            }

            candidates = [];
            return false;
        }
    }

    public bool HasActiveJobs()
    {
        return _databaseService.GetFetchJobs()
            .Any(job => job.Status is FetchJobStatus.Pending or FetchJobStatus.Running);
    }

    public async Task<FetchJob> EnqueueShowFetchAsync(long showId, CancellationToken cancellationToken = default)
    {
        var show = _databaseService.GetTrackedShow(showId) ?? throw new InvalidOperationException("Tracked show was not found.");
        var targetEpisodes = GetTargetEpisodes(showId);
        var job = new FetchJob
        {
            ShowId = show.Id,
            TargetKind = MediaKind.TvEpisode,
            ShowTitle = show.DisplayTitle,
            Status = FetchJobStatus.Pending,
            TotalEpisodes = targetEpisodes.Count,
            ProcessedEpisodes = 0,
            CreatedUtc = DateTime.UtcNow
        };
        job.Id = _databaseService.CreateFetchJob(job);
        JobsChanged?.Invoke(this, EventArgs.Empty);
        await EnsureQueueLoopAsync(cancellationToken);
        return job;
    }

    public async Task<FetchJob> EnqueueMovieFetchAsync(long movieId, CancellationToken cancellationToken = default)
    {
        var movie = _databaseService.GetTrackedMovie(movieId) ?? throw new InvalidOperationException("Tracked movie was not found.");
        var shouldFetch = movie.IsWanted && movie.Availability == EpisodeAvailability.Missing;
        var job = new FetchJob
        {
            ShowId = movie.Id,
            TargetKind = MediaKind.Movie,
            ShowTitle = movie.DisplayTitle,
            Status = FetchJobStatus.Pending,
            TotalEpisodes = shouldFetch ? 1 : 0,
            ProcessedEpisodes = 0,
            CreatedUtc = DateTime.UtcNow
        };
        job.Id = _databaseService.CreateFetchJob(job);
        JobsChanged?.Invoke(this, EventArgs.Empty);
        await EnsureQueueLoopAsync(cancellationToken);
        return job;
    }

    public void CancelJob(long jobId)
    {
        var job = _databaseService.GetFetchJob(jobId);
        if (job is null)
        {
            return;
        }

        if (job.Status == FetchJobStatus.Running)
        {
            _activeJobCancellation?.Cancel();
            return;
        }

        if (job.Status == FetchJobStatus.Pending)
        {
            job.Status = FetchJobStatus.Canceled;
            job.FinishedUtc = DateTime.UtcNow;
            _databaseService.UpdateFetchJob(job);
            JobsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void CancelActiveJobs()
    {
        _activeJobCancellation?.Cancel();
        foreach (var job in _databaseService.GetFetchJobs()
                     .Where(job => job.Status is FetchJobStatus.Pending or FetchJobStatus.Running))
        {
            job.Status = FetchJobStatus.Canceled;
            job.FinishedUtc = DateTime.UtcNow;
            job.ErrorSummary = "Canceled because the app is closing.";
            _databaseService.UpdateFetchJob(job);
        }

        JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<FetchJob> RetryJobAsync(long jobId, CancellationToken cancellationToken = default)
    {
        var job = _databaseService.GetFetchJob(jobId) ?? throw new InvalidOperationException("Fetch job was not found.");
        return job.TargetKind == MediaKind.Movie
            ? await EnqueueMovieFetchAsync(job.ShowId, cancellationToken)
            : await EnqueueShowFetchAsync(job.ShowId, cancellationToken);
    }

    public void DeleteJob(long jobId)
    {
        CancelJob(jobId);
        _databaseService.DeleteFetchJob(jobId);
        JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task EnsureQueueLoopAsync(CancellationToken cancellationToken)
    {
        await _queueSignal.WaitAsync(cancellationToken);
        try
        {
            if (_queueLoopRunning)
            {
                return;
            }

            _queueLoopRunning = true;
            _ = Task.Run(ProcessQueueAsync, CancellationToken.None);
        }
        finally
        {
            _queueSignal.Release();
        }
    }

    private void StartQueueLoopIfNeeded()
    {
        lock (_gate)
        {
            if (_queueLoopRunning)
            {
                return;
            }

            _queueLoopRunning = true;
            _ = Task.Run(ProcessQueueAsync, CancellationToken.None);
        }
    }

    private void RecoverPersistedJobs()
    {
        var jobs = _databaseService.GetFetchJobs();
        foreach (var job in jobs.Where(job => job.Status == FetchJobStatus.Running))
        {
            job.Status = FetchJobStatus.Failed;
            job.FinishedUtc = DateTime.UtcNow;
            job.ErrorSummary = "App closed while this job was running.";
            _databaseService.UpdateFetchJob(job);
        }

        if (jobs.Any(job => job.Status == FetchJobStatus.Pending))
        {
            StartQueueLoopIfNeeded();
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            while (true)
            {
                var job = _databaseService.GetFetchJobs()
                    .OrderBy(candidate => candidate.CreatedUtc)
                    .FirstOrDefault(candidate => candidate.Status == FetchJobStatus.Pending);
                if (job is null)
                {
                    return;
                }

                _activeJobCancellation = new CancellationTokenSource();
                await ProcessJobAsync(job, _activeJobCancellation.Token);
                _activeJobCancellation.Dispose();
                _activeJobCancellation = null;
            }
        }
        finally
        {
            _queueLoopRunning = false;
            JobsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ProcessJobAsync(FetchJob job, CancellationToken cancellationToken)
    {
        job.Status = FetchJobStatus.Running;
        job.StartedUtc = DateTime.UtcNow;
        job.ErrorSummary = null;
        _databaseService.UpdateFetchJob(job);
        JobsChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            if (job.TargetKind == MediaKind.Movie)
            {
                await ProcessMovieJobAsync(job, cancellationToken);
                return;
            }

            var show = _databaseService.GetTrackedShow(job.ShowId) ?? throw new InvalidOperationException("Tracked show was not found.");
            var targetEpisodes = GetTargetEpisodes(job.ShowId);
            job.TotalEpisodes = targetEpisodes.Count;
            _databaseService.UpdateFetchJob(job);
            JobsChanged?.Invoke(this, EventArgs.Empty);

            await ProcessShowJobParallelAsync(job, show, targetEpisodes, cancellationToken);

            job.Status = FetchJobStatus.Completed;
            job.FinishedUtc = DateTime.UtcNow;
            _databaseService.UpdateFetchJob(job);
            JobsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            job.Status = FetchJobStatus.Canceled;
            job.FinishedUtc = DateTime.UtcNow;
            job.ErrorSummary = "Canceled by user.";
            _databaseService.UpdateFetchJob(job);
            JobsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            job.Status = FetchJobStatus.Failed;
            job.FinishedUtc = DateTime.UtcNow;
            job.ErrorSummary = ex.Message;
            _databaseService.UpdateFetchJob(job);
            _logger.Error($"Fetch job failed for {job.ShowTitle}", ex, LogTarget.All);
            JobsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ProcessShowJobParallelAsync(
        FetchJob job,
        TrackedShow show,
        IReadOnlyList<TrackedEpisode> targetEpisodes,
        CancellationToken cancellationToken)
    {
        var parallelSearches = Math.Min(GetMaxParallelSearches(), Math.Max(targetEpisodes.Count, 1));
        var nextIndex = 0;
        _logger.Info(
            $"Starting fetch job #{job.Id} for {show.DisplayTitle}. TargetEpisodes={targetEpisodes.Count}, ParallelSearches={parallelSearches}.",
            LogTarget.All);

        async Task RunWorkerAsync(int workerId)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = Interlocked.Increment(ref nextIndex) - 1;
                if (index >= targetEpisodes.Count)
                {
                    return;
                }

                await ProcessEpisodeSearchAsync(job, show, targetEpisodes[index], workerId, cancellationToken);
            }
        }

        var workers = Enumerable.Range(1, parallelSearches)
            .Select(RunWorkerAsync)
            .ToList();

        await Task.WhenAll(workers);
    }

    private async Task ProcessEpisodeSearchAsync(
        FetchJob job,
        TrackedShow show,
        TrackedEpisode episode,
        int workerId,
        CancellationToken cancellationToken)
    {
        var queries = BuildQueries(show, episode).ToList();
        var query = string.Join(" | ", queries);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.Info($"Worker {workerId} fetching candidates for {query}", LogTarget.All);
        var selectedQualities = ParseQualities(show.PreferredQuality);
        var searchSummary = await SearchEpisodeCandidatesSequentialAsync(episode, show, queries, selectedQualities, cancellationToken);
        var candidates = searchSummary.Candidates;

        stopwatch.Stop();
        LogCandidateSummary(query, episode, searchSummary.SearchResults, candidates);
        _logger.Info(
            $"Worker {workerId} finished {query}. Duration={stopwatch.Elapsed.TotalSeconds:0.0}s, RawResults={searchSummary.SearchResults.Count}, AcceptedCandidates={candidates.Count}, Rejected={Math.Max(0, searchSummary.SearchResults.Count - candidates.Count)}.",
            LogTarget.All);

        lock (_gate)
        {
            _candidatesByEpisodeId[episode.Id] = candidates;
            job.ProcessedEpisodes++;
            _databaseService.UpdateFetchJob(job);
        }

        CandidatesChanged?.Invoke(this, EventArgs.Empty);
        JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ProcessMovieJobAsync(FetchJob job, CancellationToken cancellationToken)
    {
        var movie = _databaseService.GetTrackedMovie(job.ShowId) ?? throw new InvalidOperationException("Tracked movie was not found.");
        if (!movie.IsWanted || movie.Availability != EpisodeAvailability.Missing)
        {
            job.TotalEpisodes = 0;
            job.ProcessedEpisodes = 0;
            job.Status = FetchJobStatus.Completed;
            job.FinishedUtc = DateTime.UtcNow;
            _databaseService.UpdateFetchJob(job);
            JobsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        job.TotalEpisodes = 1;
        _databaseService.UpdateFetchJob(job);
        JobsChanged?.Invoke(this, EventArgs.Empty);

        cancellationToken.ThrowIfCancellationRequested();
        var queries = BuildMovieQueries(movie).ToList();
        var query = string.Join(" | ", queries);
        _logger.Info($"Fetching movie candidates for {query}", LogTarget.All);
        var searchResults = await SearchManyAsync(queries, cancellationToken);
        var filteredResults = searchResults
            .Where(result => IsUsableMovieCandidate(result, movie, query));
        var candidates = filteredResults
            .Select(result => ToMovieCandidate(movie.Id, result, movie.PreferredQuality, movie.PreferredAudioCodec))
            .OrderByDescending(candidate => candidate.TotalScore)
            .ThenByDescending(candidate => candidate.Seeders)
            .Take(GetMaxCandidatesPerFetch())
            .ToList();

        _logger.Info(
            $"Mapped {candidates.Count} movie candidate(s) for {query}. SearchResults={searchResults.Count}, MovieId={movie.Id}.",
            LogTarget.All);
        lock (_gate)
        {
            _candidatesByMovieId[movie.Id] = candidates;
        }

        job.ProcessedEpisodes = 1;
        job.Status = FetchJobStatus.Completed;
        job.FinishedUtc = DateTime.UtcNow;
        _databaseService.UpdateFetchJob(job);
        CandidatesChanged?.Invoke(this, EventArgs.Empty);
        JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<TrackedEpisode> GetTargetEpisodes(long showId)
    {
        return _databaseService.GetTrackedEpisodes(showId)
            .Where(episode => episode.IsWanted && episode.Availability == EpisodeAvailability.Missing)
            .OrderBy(episode => episode.SeasonNumber)
            .ThenBy(episode => episode.EpisodeNumber)
            .ToList();
    }

    private static IEnumerable<string> BuildQueries(TrackedShow show, TrackedEpisode episode)
    {
        var qualities = ParseQualities(show.PreferredQuality).DefaultIfEmpty(string.Empty).ToList();
        foreach (var quality in qualities)
        {
            var baseParts = new List<string>
            {
                show.Title,
                $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}",
                quality,
                show.PreferredAudioCodec
            };
            yield return string.Join(' ', baseParts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

            if (show.FirstAirYear is not null)
            {
                var yearParts = new List<string>
                {
                    show.Title,
                    show.FirstAirYear.Value.ToString(),
                    $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}",
                    quality,
                    show.PreferredAudioCodec
                };
                yield return string.Join(' ', yearParts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
            }

            var simpleParts = new List<string>
            {
                show.Title,
                $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}"
            };
            yield return string.Join(' ', simpleParts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
        }
    }

    private static IEnumerable<string> BuildMovieQueries(TrackedMovie movie)
    {
        var qualities = ParseQualities(movie.PreferredQuality).DefaultIfEmpty(string.Empty);
        foreach (var quality in qualities)
        {
            yield return string.Join(' ', new[]
            {
                movie.Title,
                movie.ReleaseYear?.ToString(),
                quality,
                movie.PreferredAudioCodec
            }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
        }
    }

    private async Task<IReadOnlyList<TorrentSearchResult>> SearchManyAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken)
    {
        var results = new List<TorrentSearchResult>();
        foreach (var query in queries.Where(query => !string.IsNullOrWhiteSpace(query)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (var attempt = 1; attempt <= SearchCapacityRetryCount; attempt++)
            {
                try
                {
                    results.AddRange(await _qbittorrentClient.SearchAsync(new TorrentSearchRequest { Query = query }, cancellationToken));
                    break;
                }
                catch (QbittorrentSearchCapacityException) when (attempt < SearchCapacityRetryCount)
                {
                    var delay = TimeSpan.FromMilliseconds(SearchCapacityRetryDelayMilliseconds * attempt);
                    _logger.Warning(
                        $"qBittorrent search capacity is full. Retrying query '{query}' in {delay.TotalSeconds:0}s. Attempt={attempt}/{SearchCapacityRetryCount}.",
                        LogTarget.All);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        return results
            .GroupBy(result => result.FileUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(result => result.Seeders).First())
            .ToList();
    }

    private sealed class EpisodeSearchSummary
    {
        public EpisodeSearchSummary(IReadOnlyList<EpisodeFetchCandidate> candidates, IReadOnlyList<TorrentSearchResult> searchResults)
        {
            Candidates = candidates;
            SearchResults = searchResults;
        }

        public IReadOnlyList<EpisodeFetchCandidate> Candidates { get; }

        public IReadOnlyList<TorrentSearchResult> SearchResults { get; }
    }

    private async Task<EpisodeSearchSummary> SearchEpisodeCandidatesSequentialAsync(
        TrackedEpisode episode,
        TrackedShow show,
        IReadOnlyList<string> queries,
        IReadOnlyList<string> selectedQualities,
        CancellationToken cancellationToken)
    {
        var maxCandidates = GetMaxCandidatesPerFetch();
        var resultsByUrl = new Dictionary<string, TorrentSearchResult>(StringComparer.OrdinalIgnoreCase);
        var matchedCandidates = new List<(EpisodeFetchCandidate Candidate, CandidateMatchResult Match)>();

        foreach (var query in queries.Where(query => !string.IsNullOrWhiteSpace(query)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var queryResults = await SearchSingleQueryAsync(query, cancellationToken);
            foreach (var result in queryResults)
            {
                if (!string.IsNullOrWhiteSpace(result.FileUrl))
                {
                    resultsByUrl[result.FileUrl] = result;
                }

                if (matchedCandidates.Count >= maxCandidates)
                {
                    continue;
                }

                var match = CandidateMatcher.MatchEpisodeCandidate(show, episode, result, selectedQualities);
                if (!match.IsAccepted)
                {
                    var message =
                        $"Rejected search candidate for '{query}'. Reason='{match.RejectReason}', Engine='{result.EngineName}', Name='{result.FileName}', Url='{result.FileUrl}'.";
                    if (match.RejectReason?.Contains("plugin error", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _logger.Warning(message, LogTarget.All);
                    }
                    else
                    {
                        _logger.Debug(message, LogTarget.File | LogTarget.Console);
                    }
                    continue;
                }

                var candidate = ToCandidate(episode.Id, result, match.QualityScore, match.TotalScore);
                matchedCandidates.Add((candidate, match));
            }

            if (matchedCandidates.Count >= maxCandidates)
            {
                break;
            }
        }

        var candidates = matchedCandidates
            .OrderByDescending(entry => entry.Match.TotalScore)
            .ThenByDescending(entry => entry.Candidate.Seeders)
            .Take(maxCandidates)
            .Select(entry => entry.Candidate)
            .ToList();

        var searchResults = resultsByUrl.Values
            .OrderByDescending(result => result.Seeders)
            .ToList();

        return new EpisodeSearchSummary(candidates, searchResults);
    }

    private async Task<IReadOnlyList<TorrentSearchResult>> SearchSingleQueryAsync(string query, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= SearchCapacityRetryCount; attempt++)
        {
            try
            {
                return await _qbittorrentClient.SearchAsync(new TorrentSearchRequest { Query = query }, cancellationToken);
            }
            catch (QbittorrentSearchCapacityException) when (attempt < SearchCapacityRetryCount)
            {
                var delay = TimeSpan.FromMilliseconds(SearchCapacityRetryDelayMilliseconds * attempt);
                _logger.Warning(
                    $"qBittorrent search capacity is full. Retrying query '{query}' in {delay.TotalSeconds:0}s. Attempt={attempt}/{SearchCapacityRetryCount}.",
                    LogTarget.All);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return [];
    }

    private bool IsUsableMovieCandidate(TorrentSearchResult result, TrackedMovie movie, string query)
    {
        var rejectionReason = GetMovieCandidateRejectionReason(result, movie);
        if (rejectionReason is null)
        {
            return true;
        }

        var message =
            $"Rejected movie search candidate for '{query}'. Reason='{rejectionReason}', Engine='{result.EngineName}', Name='{result.FileName}', Url='{result.FileUrl}'.";
        if (rejectionReason.Contains("plugin error", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning(message, LogTarget.All);
        }
        else
        {
            _logger.Debug(message, LogTarget.File | LogTarget.Console);
        }

        return false;
    }

    private static string? GetMovieCandidateRejectionReason(TorrentSearchResult result, TrackedMovie movie)
    {
        if (!result.CanAdd)
        {
            return $"not addable link type '{result.LinkType}'";
        }

        if (LooksLikePluginError(result.FileName))
        {
            return "search plugin error row";
        }

        if (result.Seeders < movie.MinimumSeeders)
        {
            return $"seeders below threshold {movie.MinimumSeeders}";
        }

        var selectedQualities = ParseQualities(movie.PreferredQuality);
        if (selectedQualities.Count > 0 && !selectedQualities.Any(quality => result.FileName.Contains(quality, StringComparison.OrdinalIgnoreCase)))
        {
            return $"does not match selected quality options: {string.Join(", ", selectedQualities)}";
        }

        if (movie.ReleaseYear is not null && !result.FileName.Contains(movie.ReleaseYear.Value.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return $"does not contain release year {movie.ReleaseYear}";
        }

        if (!LooksLikeMovieTitleMatch(result.FileName, movie.Title))
        {
            return "does not contain enough movie title tokens";
        }

        return null;
    }

    private void LogCandidateSummary(
        string query,
        TrackedEpisode episode,
        IReadOnlyList<TorrentSearchResult> searchResults,
        IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        if (candidates.Count > 0)
        {
            _logger.Info(
                $"Mapped {candidates.Count} candidate(s) for {query}. SearchResults={searchResults.Count}, EpisodeId={episode.Id}.",
                LogTarget.All);
            return;
        }

        _logger.Warning(
            $"No usable candidates for {query}. SearchResults={searchResults.Count}, EpisodeId={episode.Id}. Check earlier rejected-candidate logs for plugin errors or title mismatch.",
            LogTarget.All);
    }

    private static bool LooksLikePluginError(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        return fileName.Contains("api key error", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("right-click this row", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("open description", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("jackett:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMovieTitleMatch(string fileName, string title)
    {
        var fileTokens = Tokenize(fileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var titleTokens = Tokenize(title).Where(token => token.Length > 2).ToList();
        if (titleTokens.Count == 0)
        {
            return true;
        }

        var matched = titleTokens.Count(token => fileTokens.Contains(token));
        return matched >= Math.Min(2, titleTokens.Count);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Kept for potential reuse after sequential search refactor.")]
    private List<EpisodeFetchCandidate> MapEpisodeCandidates(
        TrackedEpisode episode,
        TrackedShow show,
        string query,
        IReadOnlyList<TorrentSearchResult> searchResults,
        IReadOnlyList<string> selectedQualities)
    {
        var matchedCandidates = new List<(EpisodeFetchCandidate Candidate, CandidateMatchResult Match)>();
        foreach (var result in searchResults)
        {
            var match = CandidateMatcher.MatchEpisodeCandidate(show, episode, result, selectedQualities);
            if (!match.IsAccepted)
            {
                var message =
                    $"Rejected search candidate for '{query}'. Reason='{match.RejectReason}', Engine='{result.EngineName}', Name='{result.FileName}', Url='{result.FileUrl}'.";
                if (match.RejectReason?.Contains("plugin error", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.Warning(message, LogTarget.All);
                }
                else
                {
                    _logger.Debug(message, LogTarget.File | LogTarget.Console);
                }

                continue;
            }

            var candidate = ToCandidate(episode.Id, result, match.QualityScore, match.TotalScore);
            matchedCandidates.Add((candidate, match));
        }

        return matchedCandidates
            .OrderByDescending(entry => entry.Match.TotalScore)
            .ThenByDescending(entry => entry.Candidate.Seeders)
            .Take(GetMaxCandidatesPerFetch())
            .Select(entry => entry.Candidate)
            .ToList();
    }

    private static EpisodeFetchCandidate ToCandidate(
        long episodeId,
        TorrentSearchResult result,
        int qualityScore,
        int totalScore)
    {
        return new EpisodeFetchCandidate
        {
            EpisodeId = episodeId,
            FileName = result.FileName,
            FileSize = result.FileSize,
            FileUrl = result.FileUrl,
            QualityLabel = DetectQuality(result.FileName),
            AudioCodecLabel = DetectAudioCodec(result.FileName),
            Seeders = result.Seeders,
            Leechers = result.Leechers,
            PluginName = result.EngineName,
            QualityScore = qualityScore,
            TotalScore = totalScore
        };
    }

    private static EpisodeFetchCandidate ToMovieCandidate(
        long movieId,
        TorrentSearchResult result,
        string preferredQuality,
        string preferredAudioCodec)
    {
        var selectedQualities = ParseQualities(preferredQuality);
        var qualityScore = selectedQualities.Count > 0 &&
                           selectedQualities.Any(quality => result.FileName.Contains(quality, StringComparison.OrdinalIgnoreCase))
            ? 1
            : 0;
        var audioScore = !string.IsNullOrWhiteSpace(preferredAudioCodec) &&
                         result.FileName.Contains(preferredAudioCodec, StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
        var totalScore = qualityScore * 100000 + audioScore * 25000 + result.Seeders;
        var candidate = ToCandidate(0, result, qualityScore, totalScore);
        candidate.MovieId = movieId;
        return candidate;
    }

    private int GetMaxCandidatesPerFetch()
    {
        return Math.Clamp(_settingsService.Current.AutoTorrent.MaxCandidatesPerFetch, 1, 10);
    }

    private int GetMaxParallelSearches()
    {
        return Math.Clamp(_settingsService.Current.AutoTorrent.MaxParallelSearches, 1, 4);
    }

    private static IReadOnlyList<string> ParseQualities(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(quality => !string.IsNullOrWhiteSpace(quality))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        return value
            .Replace('.', ' ')
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => !string.IsNullOrWhiteSpace(token));
    }

    private static string DetectQuality(string fileName)
    {
        string[] qualities = ["2160p", "1080p", "720p", "480p"];
        return qualities.FirstOrDefault(quality => fileName.Contains(quality, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static string DetectAudioCodec(string fileName)
    {
        string[] codecs = ["TrueHD", "Atmos", "DTS-HD", "DTS", "DDP", "DD+", "AAC", "AC3", "FLAC", "Opus"];
        return codecs.FirstOrDefault(codec => fileName.Contains(codec, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }
}
