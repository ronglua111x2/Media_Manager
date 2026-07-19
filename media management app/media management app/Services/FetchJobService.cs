using System.Text.RegularExpressions;
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
    private readonly IRecipeService _recipeService;
    private readonly ISearchPlanBuilder _searchPlanBuilder;
    private readonly ICandidateEvaluationService _candidateEvaluationService;
    private readonly ISearchTitleResolver _titleResolver;
    private readonly ShowSearchSnapshotService _snapshotService;
    private readonly IOperationProgressService _progressService;
    private readonly IAppLogger _logger;
    private readonly Dictionary<long, IReadOnlyList<EpisodeFetchCandidate>> _candidatesByEpisodeId = [];
    private readonly Dictionary<long, IReadOnlyList<EpisodeFetchCandidate>> _candidatesByMovieId = [];
    private readonly Dictionary<(long ShowId, int SeasonNumber), IReadOnlyList<SeasonPackCandidate>> _packCandidatesBySeason = [];
    private readonly object _gate = new();

    public FetchJobService(
        IDatabaseService databaseService,
        ISettingsService settingsService,
        IQbittorrentClient qbittorrentClient,
        IRecipeService recipeService,
        ISearchPlanBuilder searchPlanBuilder,
        ICandidateEvaluationService candidateEvaluationService,
        ISearchTitleResolver titleResolver,
        ShowSearchSnapshotService snapshotService,
        IOperationProgressService progressService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _settingsService = settingsService;
        _qbittorrentClient = qbittorrentClient;
        _recipeService = recipeService;
        _searchPlanBuilder = searchPlanBuilder;
        _candidateEvaluationService = candidateEvaluationService;
        _titleResolver = titleResolver;
        _snapshotService = snapshotService;
        _progressService = progressService;
        _logger = logger;
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

    public bool TryGetPackCandidates(long showId, int seasonNumber, out IReadOnlyList<SeasonPackCandidate> candidates)
    {
        lock (_gate)
        {
            if (_packCandidatesBySeason.TryGetValue((showId, seasonNumber), out var storedCandidates))
            {
                candidates = storedCandidates;
                return true;
            }

            candidates = [];
            return false;
        }
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<EpisodeFetchCandidate>>> FetchEpisodeCandidatesAsync(
        long showId,
        IReadOnlyList<long> episodeIds,
        string? recipeId = null,
        Action<long, string>? statusChanged = null,
        CancellationToken cancellationToken = default,
        EpisodeFetchOptions? options = null)
    {
        var show = _databaseService.GetTrackedShow(showId) ?? throw new InvalidOperationException("Tracked show was not found.");
        var requestedEpisodeIds = episodeIds.Distinct().ToHashSet();
        var targetEpisodes = _databaseService.GetTrackedEpisodes(showId)
            .Where(episode => requestedEpisodeIds.Contains(episode.Id))
            .OrderBy(episode => episode.SeasonNumber)
            .ThenBy(episode => episode.EpisodeNumber)
            .ToList();
        if (targetEpisodes.Count == 0)
        {
            return new Dictionary<long, IReadOnlyList<EpisodeFetchCandidate>>();
        }

        ClearEpisodeCandidateCache(targetEpisodes);
        var recipe = _recipeService.GetRecipeOrDefault(recipeId ?? show.RecipeId, MediaKind.TvEpisode);
        var useSnapshot = options?.ForceParallelEpisodeSearch == true
            ? false
            : RecipeRuntimeSettings.GetUseShowSnapshotSearch(recipe, _settingsService.Current.AutoTorrent);
        var mode = useSnapshot ? "Show snapshot search" : "Parallel episode search";
        _logger.Info(
            $"Cart episode run for {show.DisplayTitle}. Recipe='{recipe.Name}', Mode='{mode}', Orders={targetEpisodes.Count}.",
            LogTarget.All);

        return useSnapshot
            ? await FetchEpisodeCandidatesSnapshotAsync(show, targetEpisodes, recipe, statusChanged, cancellationToken)
            : await FetchEpisodeCandidatesParallelAsync(show, targetEpisodes, recipe, statusChanged, cancellationToken, options?.MaxParallelWorkers);
    }

    public async Task FetchSeasonPacksAsync(long showId, IReadOnlyList<int> seasonNumbers, CancellationToken cancellationToken = default, int? maxCandidatesOverride = null)
    {
        var show = _databaseService.GetTrackedShow(showId) ?? throw new InvalidOperationException("Tracked show was not found.");
        var hiddenSeasons = _databaseService.GetTrackedSeasons(showId)
            .Where(season => season.IsHidden)
            .Select(season => season.SeasonNumber)
            .ToHashSet();
        var selectedSeasons = seasonNumbers
            .Where(season => season > 0 && !hiddenSeasons.Contains(season))
            .Distinct()
            .Order()
            .ToList();
        if (selectedSeasons.Count == 0)
        {
            throw new InvalidOperationException("Select at least one pack-mode season.");
        }

        var packRecipe = _recipeService.GetRecipeOrDefault(show.PackRecipeId, MediaKind.TvSeasonPack);
        var snapshotResults = await _snapshotService.CaptureSnapshotAsync(
            show,
            _progressService,
            cancellationToken,
            MediaKind.TvSeasonPack);
        var candidates = await MapSeasonPackCandidatesAsync(
            show,
            selectedSeasons,
            snapshotResults,
            packRecipe,
            maxCandidatesOverride,
            cancellationToken);
        lock (_gate)
        {
            foreach (var seasonNumber in selectedSeasons)
            {
                _packCandidatesBySeason[(showId, seasonNumber)] = candidates
                    .Where(candidate => candidate.CoveredSeasons.Contains(seasonNumber))
                    .ToList();
            }
        }

        _logger.Info($"Fetched season pack candidates for {show.DisplayTitle}. Seasons={string.Join(",", selectedSeasons)}, Candidates={candidates.Count}.", LogTarget.All);
    }

    private void ClearEpisodeCandidateCache(IReadOnlyList<TrackedEpisode> targetEpisodes)
    {
        if (targetEpisodes.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var episode in targetEpisodes)
            {
                _candidatesByEpisodeId[episode.Id] = [];
            }
        }
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<EpisodeFetchCandidate>>> FetchEpisodeCandidatesParallelAsync(
        TrackedShow show,
        IReadOnlyList<TrackedEpisode> targetEpisodes,
        SearchRecipe recipe,
        Action<long, string>? statusChanged,
        CancellationToken cancellationToken,
        int? maxParallelWorkersOverride = null)
    {
        var parallelSearches = maxParallelWorkersOverride is > 0
            ? Math.Clamp(maxParallelWorkersOverride.Value, 1, 4)
            : Math.Min(
                RecipeRuntimeSettings.GetParallelSearchCount(recipe, _settingsService.Current.AutoTorrent),
                Math.Max(targetEpisodes.Count, 1));
        var nextIndex = 0;
        var results = new Dictionary<long, IReadOnlyList<EpisodeFetchCandidate>>();
        var resultGate = new object();

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

                var episode = targetEpisodes[index];
                var label = $"{show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
                statusChanged?.Invoke(episode.Id, $"Searching {label} with {recipe.Name} (parallel worker {workerId}).");
                _logger.Info($"Cart parallel worker {workerId} searching {label} with recipe '{recipe.Name}'.", LogTarget.All);

                var queries = _searchPlanBuilder.BuildEpisodeQueries(recipe, show, episode).ToList();
                var searchSummary = await SearchEpisodeCandidatesSequentialAsync(recipe, episode, show, queries, cancellationToken);
                lock (_gate)
                {
                    _candidatesByEpisodeId[episode.Id] = searchSummary.Candidates;
                }

                lock (resultGate)
                {
                    results[episode.Id] = searchSummary.Candidates;
                }

                var detail = searchSummary.Candidates.Count == 0
                    ? $"No candidates found for {label}."
                    : $"Found {searchSummary.Candidates.Count} candidate(s) for {label}.";
                statusChanged?.Invoke(episode.Id, detail);
                _logger.Info(
                    $"Cart parallel search finished {label}. RawResults={searchSummary.SearchResults.Count}, Candidates={searchSummary.Candidates.Count}.",
                    LogTarget.All);
                await Task.Yield();
            }
        }

        var workers = Enumerable.Range(1, parallelSearches)
            .Select(RunWorkerAsync)
            .ToList();
        await Task.WhenAll(workers);
        return results;
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<EpisodeFetchCandidate>>> FetchEpisodeCandidatesSnapshotAsync(
        TrackedShow show,
        IReadOnlyList<TrackedEpisode> targetEpisodes,
        SearchRecipe recipe,
        Action<long, string>? statusChanged,
        CancellationToken cancellationToken)
    {
        foreach (var episode in targetEpisodes)
        {
            statusChanged?.Invoke(
                episode.Id,
                $"Searching snapshot for {show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00} with {recipe.Name}.");
        }

        _logger.Info(
            $"Cart snapshot search started for {show.DisplayTitle}. Recipe='{recipe.Name}', Orders={targetEpisodes.Count}, TargetResults={RecipeRuntimeSettings.GetSnapshotTargetResults(recipe, _settingsService.Current.AutoTorrent)}, TimeoutSeconds={RecipeRuntimeSettings.GetSnapshotTimeoutSeconds(recipe, _settingsService.Current.AutoTorrent)}, LocalWorkers={RecipeRuntimeSettings.GetLocalMatchWorkers(recipe, _settingsService.Current.AutoTorrent)}.",
            LogTarget.All);

        var snapshotResults = await _snapshotService.CaptureSnapshotAsync(show, _progressService, cancellationToken);
        var usesAnimeAbsolute = RecipeRuntimeSettings.UsesAnimeAbsoluteEpisodeNumbering(recipe);
        var snapshotCandidates = snapshotResults
            .Select(result => new SnapshotCandidate
            {
                Result = result,
                Parsed = TorrentCandidateParser.Parse(result.FileName, usesAnimeAbsolute)
            })
            .ToList();
        var matcher = new SnapshotCandidateMatcher();
        var selectedQualities = ParseQualities(show.PreferredQuality);
        var titleVariants = _titleResolver.Resolve(
            _titleResolver.CreateRequest(recipe, show.Title, show.GetSearchableAlternativeTitles()));
        var workerCount = Math.Min(
            RecipeRuntimeSettings.GetLocalMatchWorkers(recipe, _settingsService.Current.AutoTorrent),
            Math.Max(targetEpisodes.Count, 1));
        var nextIndex = 0;
        var results = new Dictionary<long, IReadOnlyList<EpisodeFetchCandidate>>();
        var resultGate = new object();
        var cartJob = new FetchJob { Id = 0, ShowId = show.Id, ShowTitle = show.DisplayTitle, TargetKind = MediaKind.TvEpisode };

        foreach (var episode in targetEpisodes)
        {
            statusChanged?.Invoke(
                episode.Id,
                $"Matching snapshot for {show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}.");
        }

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

                var episode = targetEpisodes[index];
                var label = $"{show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
                statusChanged?.Invoke(episode.Id, $"Matching {label} from snapshot (worker {workerId}).");
                var candidates = await MapSnapshotCandidatesAsync(
                    show,
                    episode,
                    snapshotCandidates,
                    matcher,
                    selectedQualities,
                    titleVariants,
                    cartJob,
                    cancellationToken,
                    recipe);

                lock (_gate)
                {
                    _candidatesByEpisodeId[episode.Id] = candidates;
                }

                lock (resultGate)
                {
                    results[episode.Id] = candidates;
                }

                var detail = candidates.Count == 0
                    ? $"No snapshot match found for {label}."
                    : $"Matched {candidates.Count} candidate(s) for {label}.";
                statusChanged?.Invoke(episode.Id, detail);
                await Task.Yield();
            }
        }

        var workers = Enumerable.Range(1, workerCount)
            .Select(RunWorkerAsync)
            .ToList();
        await Task.WhenAll(workers);
        return results;
    }

    private async Task<IReadOnlyList<TorrentSearchResult>> SearchManyAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken)
    {
        var plannedQueries = queries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalQueries = plannedQueries.Count;
        _logger.Info(
            $"SearchMany planned {totalQueries} query(ies): {string.Join(" | ", plannedQueries)}",
            LogTarget.All);

        var results = new List<TorrentSearchResult>();
        var completedQueries = 0;
        foreach (var query in plannedQueries)
        {
            for (var attempt = 1; attempt <= SearchCapacityRetryCount; attempt++)
            {
                try
                {
                    results.AddRange(await _qbittorrentClient.SearchAsync(new TorrentSearchRequest { Query = query }, cancellationToken));
                    completedQueries++;
                    _logger.Info(
                        $"Search query succeeded {completedQueries}/{totalQueries}. Remaining={totalQueries - completedQueries}. Query='{query}'.",
                        LogTarget.All);
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
        SearchRecipe recipe,
        TrackedEpisode episode,
        TrackedShow show,
        IReadOnlyList<string> queries,
        CancellationToken cancellationToken)
    {
        var maxCandidates = RecipeRuntimeSettings.GetMaxCandidatesPerFetch(recipe, _settingsService.Current.AutoTorrent);
        var resultsByUrl = new Dictionary<string, TorrentSearchResult>(StringComparer.OrdinalIgnoreCase);
        var matchedCandidates = new List<(EpisodeFetchCandidate Candidate, RecipeCandidateResult Match)>();
        var plannedQueries = queries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalQueries = plannedQueries.Count;
        var label = $"{show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
        _logger.Info(
            $"Episode search starting {totalQueries} query(ies) for {label}. Recipe='{recipe.Name}': {string.Join(" | ", plannedQueries)}",
            LogTarget.All);

        var completedQueries = 0;
        foreach (var query in plannedQueries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var queryResults = await SearchSingleQueryAsync(query, cancellationToken);
            completedQueries++;
            _logger.Info(
                $"Episode search query succeeded {completedQueries}/{totalQueries}. Remaining={totalQueries - completedQueries}. Query='{query}'. Results={queryResults.Count}.",
                LogTarget.All);

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

                var match = _candidateEvaluationService.EvaluateEpisode(recipe, show, episode, result);
                if (!match.IsAccepted)
                {
                    var message =
                        $"Rejected search candidate for '{query}'. Reason='{match.RejectReason}: {match.RejectDetail}', Engine='{result.EngineName}', Name='{result.FileName}', Url='{result.FileUrl}'.";
                    if (match.RejectReason == CandidateRejectReason.PluginError)
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
                if (completedQueries < totalQueries)
                {
                    _logger.Info(
                        $"Episode search stopped early for {label}: reached max candidates ({maxCandidates}). Completed={completedQueries}/{totalQueries}, Skipped={totalQueries - completedQueries}.",
                        LogTarget.All);
                }

                break;
            }
        }

        if (RecipeRuntimeSettings.GetDeduplicateCandidates(recipe, _settingsService.Current.AutoTorrent))
        {
            var autoTorrent = _settingsService.Current.AutoTorrent;
            matchedCandidates = DeduplicateEpisodeCandidates(
                matchedCandidates,
                RecipeRuntimeSettings.GetFuzzyDeduplicate(recipe, autoTorrent),
                RecipeRuntimeSettings.GetFuzzyDeduplicateSizeToleranceMb(recipe, autoTorrent));
        }

        var candidates = matchedCandidates
            .OrderByDescending(entry => entry.Match.QualityScore)
            .ThenByDescending(entry => entry.Match.AudioScore)
            .ThenByDescending(entry => entry.Candidate.Seeders)
            .ThenByDescending(entry => entry.Match.IdentityScore)
            .ThenByDescending(entry => entry.Match.EpisodeScore)
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
        var detectedQuality = TorrentQuality.Detect(result.FileName);
        if (!TorrentQuality.MatchesSelectedQuality(detectedQuality, selectedQualities))
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

    private async Task<string?> GetEpisodeMetadataRejectReasonAsync(
        TrackedShow show,
        TrackedEpisode episode,
        TorrentSearchResult result,
        TorrentCandidateParseResult parsed,
        int identityScore,
        CancellationToken cancellationToken)
    {
        var recipe = _recipeService.GetRecipeOrDefault(show.RecipeId, MediaKind.TvEpisode);
        if (!ShouldProbeEpisodeCandidate(recipe, result, parsed, identityScore))
        {
            return null;
        }

        var probe = await _qbittorrentClient.ProbeTorrentMetadataAsync(result, cancellationToken);
        if (!probe.IsAvailable)
        {
            _logger.Debug(
                $"Skipped metadata filter for '{result.FileName}'. Reason='{probe.Reason}'.",
                LogTarget.File | LogTarget.Console);
            return null;
        }

        var yearRejectReason = GetProbeYearRejectReason(show, probe);
        if (yearRejectReason is not null)
        {
            return yearRejectReason;
        }

        var files = GetProbeMatchFiles(probe).ToList();
        var usesAnimeAbsolute = RecipeRuntimeSettings.UsesAnimeAbsoluteEpisodeNumbering(recipe);
        if (files.Count == 1 &&
            TorrentCandidateParser.Parse($"{probe.TorrentName} {files[0].Path}", usesAnimeAbsolute) is { SeasonNumber: null, EpisodeNumber: null })
        {
            return null;
        }

        var hasTargetEpisode = files.Any(file =>
        {
            var fileParsed = TorrentCandidateParser.Parse($"{probe.TorrentName} {file.Path}", usesAnimeAbsolute);
            var episodeMatches = fileParsed.AbsoluteEpisodeNumber is not null && fileParsed.SeasonNumber is null
                ? fileParsed.AbsoluteEpisodeNumber == episode.EpisodeNumber
                : fileParsed.SeasonNumber == episode.SeasonNumber && fileParsed.EpisodeNumber == episode.EpisodeNumber;
            return episodeMatches && HasTitleTokenMatch(show.Title, fileParsed.TitleTokens);
        });

        return hasTargetEpisode
            ? null
            : usesAnimeAbsolute
                ? $"metadata files do not contain absolute episode {episode.EpisodeNumber}"
                : $"metadata files do not contain S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
    }

    private bool ShouldProbeEpisodeCandidate(
        SearchRecipe recipe,
        TorrentSearchResult result,
        TorrentCandidateParseResult parsed,
        int identityScore)
    {
        return RecipeRuntimeSettings.GetEnableCandidateMetadataProbe(recipe, _settingsService.Current.AutoTorrent) &&
               result.CanAdd &&
               !result.FileUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) &&
               (string.Equals(result.LinkType, "HTTP URL", StringComparison.OrdinalIgnoreCase) ||
                parsed.ExplicitYear is null ||
                identityScore <= 3);
    }

    private bool ShouldProbePackCandidate(SearchRecipe recipe, TorrentSearchResult result, TorrentCandidateParseResult parsed)
    {
        return RecipeRuntimeSettings.GetEnableCandidateMetadataProbe(recipe, _settingsService.Current.AutoTorrent) &&
               result.CanAdd &&
               !result.FileUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) &&
               (string.Equals(result.LinkType, "HTTP URL", StringComparison.OrdinalIgnoreCase) ||
                parsed.ExplicitYear is null ||
                parsed.CoveredSeasons.Count == 0);
    }

    private static IEnumerable<TorrentMetadataFile> GetProbeMatchFiles(TorrentMetadataProbeResult probe)
    {
        return probe.VideoFileCount > 0 ? probe.VideoFiles : probe.Files;
    }

    private static string? GetProbeYearRejectReason(TrackedShow show, TorrentMetadataProbeResult probe)
    {
        if (!probe.IsAvailable || show.FirstAirYear is null)
        {
            return null;
        }

        foreach (var file in GetProbeMatchFiles(probe))
        {
            var value = $"{probe.TorrentName} {file.Path}";
            if (TorrentCandidateParser.ContainsYearRangeIncluding(value, show.FirstAirYear.Value))
            {
                continue;
            }

            var wrongYear = ExtractExplicitYears(value)
                .FirstOrDefault(year => year != show.FirstAirYear.Value);
            if (wrongYear > 0)
            {
                return $"metadata explicit year mismatch {wrongYear} != {show.FirstAirYear}";
            }
        }

        return null;
    }

    private static IEnumerable<int> InferCoveredSeasonsFromProbe(TorrentMetadataProbeResult probe)
    {
        if (!probe.IsAvailable)
        {
            return [];
        }

        return GetProbeMatchFiles(probe)
            .Select(file => TorrentCandidateParser.Parse($"{probe.TorrentName} {file.Path}").SeasonNumber)
            .Where(season => season is >= AppConstants.SpecialsSeasonNumber)
            .Select(season => season!.Value)
            .Distinct()
            .Order()
            .ToList();
    }

    private static bool HasTitleTokenMatch(string title, IReadOnlyList<string> candidateTokens)
    {
        var titleTokens = TorrentCandidateParser.Tokenize(title).Where(token => token.Length > 2).ToList();
        if (titleTokens.Count == 0)
        {
            return true;
        }

        var matched = titleTokens.Count(token => candidateTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
        return matched >= Math.Min(2, titleTokens.Count);
    }

    private static IEnumerable<int> ExtractExplicitYears(string value)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(value, @"\b(?:19|20)\d{2}\b"))
        {
            if (int.TryParse(match.Value, out var year))
            {
                yield return year;
            }
        }
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

    private async Task<List<EpisodeFetchCandidate>> MapSnapshotCandidatesAsync(
        TrackedShow show,
        TrackedEpisode episode,
        IReadOnlyList<SnapshotCandidate> snapshotCandidates,
        SnapshotCandidateMatcher matcher,
        IReadOnlyList<string> selectedQualities,
        IReadOnlyList<string> titleVariants,
        FetchJob job,
        CancellationToken cancellationToken,
        SearchRecipe? recipeOverride = null)
    {
        var matchedCandidates = new List<(EpisodeFetchCandidate Candidate, SnapshotMatchResult Match)>();
        var recipe = recipeOverride ?? _recipeService.GetRecipeOrDefault(show.RecipeId, MediaKind.TvEpisode);
        var scoringWeights = RecipeRuntimeSettings.GetCandidateScoringWeights(recipe);
        foreach (var candidate in snapshotCandidates)
        {
            var match = matcher.Match(show, episode, candidate, selectedQualities, titleVariants, scoringWeights);
            if (!match.IsAccepted)
            {
                if (match.RejectReason?.Contains("plugin error", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.Warning(
                        $"Snapshot search plugin error row for '{show.DisplayTitle} {episode.SeasonNumber:00}x{episode.EpisodeNumber:00}'. Engine='{candidate.Result.EngineName}', Name='{candidate.Result.FileName}'.",
                        LogTarget.All);
                }

                continue;
            }

            var metadataRejectReason = await GetEpisodeMetadataRejectReasonAsync(
                show,
                episode,
                candidate.Result,
                candidate.Parsed,
                match.IdentityScore,
                cancellationToken);
            if (metadataRejectReason is not null)
            {
                continue;
            }

            var episodeCandidate = ToCandidate(episode.Id, candidate.Result, match.QualityScore, match.TotalScore);
            matchedCandidates.Add((episodeCandidate, match));
        }

        var maxCandidates = RecipeRuntimeSettings.GetMaxCandidatesPerFetch(recipe, _settingsService.Current.AutoTorrent);
        if (RecipeRuntimeSettings.GetDeduplicateCandidates(recipe, _settingsService.Current.AutoTorrent))
        {
            var autoTorrent = _settingsService.Current.AutoTorrent;
            matchedCandidates = DeduplicateEpisodeCandidates(
                matchedCandidates,
                RecipeRuntimeSettings.GetFuzzyDeduplicate(recipe, autoTorrent),
                RecipeRuntimeSettings.GetFuzzyDeduplicateSizeToleranceMb(recipe, autoTorrent));
        }

        var finalCandidates = matchedCandidates
            .OrderByDescending(entry => entry.Match.QualityScore)
            .ThenByDescending(entry => entry.Match.AudioScore)
            .ThenByDescending(entry => entry.Candidate.Seeders)
            .ThenByDescending(entry => entry.Match.IdentityScore)
            .ThenByDescending(entry => entry.Match.EpisodeScore)
            .Take(maxCandidates)
            .Select(entry => entry.Candidate)
            .ToList();

        _logger.Info(
            $"Snapshot matching complete for {show.DisplayTitle} {episode.SeasonNumber:00}x{episode.EpisodeNumber:00}. Candidates={finalCandidates.Count}, SnapshotSize={snapshotCandidates.Count}, JobId={job.Id}.",
            LogTarget.All);

        return finalCandidates;
    }

    private async Task<List<SeasonPackCandidate>> MapSeasonPackCandidatesAsync(
        TrackedShow show,
        IReadOnlyList<int> selectedSeasons,
        IReadOnlyList<TorrentSearchResult> snapshotResults,
        SearchRecipe packRecipe,
        int? maxCandidatesOverride,
        CancellationToken cancellationToken)
    {
        var selectedQualities = ParseQualities(show.PreferredQuality);
        var maxCandidates = maxCandidatesOverride ??
            RecipeRuntimeSettings.GetMaxCandidatesPerFetch(packRecipe, _settingsService.Current.AutoTorrent);
        var candidates = new List<SeasonPackCandidate>();
        foreach (var result in snapshotResults)
        {
            var parsed = TorrentCandidateParser.Parse(result.FileName);
            var rejectReason = GetPackRejectReason(show, selectedSeasons, result, parsed, selectedQualities);
            var coveredSeasons = parsed.CoveredSeasons.ToList();
            TorrentMetadataProbeResult? probe = null;

            if (rejectReason is "no explicit season coverage" && ShouldProbePackCandidate(packRecipe, result, parsed))
            {
                probe = await _qbittorrentClient.ProbeTorrentMetadataAsync(result, cancellationToken);
                var probeRejectReason = GetProbeYearRejectReason(show, probe);
                if (probeRejectReason is not null)
                {
                    rejectReason = probeRejectReason;
                }
                else
                {
                    var probedSeasons = InferCoveredSeasonsFromProbe(probe).ToList();
                    if (probedSeasons.Count > 0)
                    {
                        coveredSeasons = probedSeasons;
                        rejectReason = GetPackRejectReason(show, selectedSeasons, result, parsed, selectedQualities, coveredSeasons);
                        _logger.Debug(
                            $"Pack metadata probe inferred seasons for '{show.DisplayTitle}'. Seasons={string.Join(",", coveredSeasons)}, Name='{result.FileName}'.",
                            LogTarget.File | LogTarget.Console);
                    }
                }
            }
            else if (rejectReason is null && ShouldProbePackCandidate(packRecipe, result, parsed))
            {
                probe = await _qbittorrentClient.ProbeTorrentMetadataAsync(result, cancellationToken);
                rejectReason = GetProbeYearRejectReason(show, probe);
                var probedSeasons = InferCoveredSeasonsFromProbe(probe).ToList();
                if (rejectReason is null && probedSeasons.Count > 0 && !probedSeasons.Any(selectedSeasons.Contains))
                {
                    rejectReason = $"metadata files do not cover selected seasons {string.Join(",", selectedSeasons)}";
                }
            }

            if (rejectReason is not null)
            {
                _logger.Debug($"Rejected pack candidate for '{show.DisplayTitle}'. Reason='{rejectReason}', Name='{result.FileName}', Url='{result.FileUrl}'.", LogTarget.File | LogTarget.Console);
                continue;
            }

            if (probe is null &&
                RecipeRuntimeSettings.GetEnableCandidateMetadataProbe(packRecipe, _settingsService.Current.AutoTorrent) &&
                result.CanAdd &&
                !result.FileUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            {
                probe = await _qbittorrentClient.ProbeTorrentMetadataAsync(result, cancellationToken);
            }

            var contentProfile = PackContentAnalyzer.Analyze(result.FileName, probe, coveredSeasons);
            if (contentProfile.CoveredSeasons.Count > 0)
            {
                coveredSeasons = contentProfile.CoveredSeasons.ToList();
            }

            var matchingSeasonCount = coveredSeasons.Count(selectedSeasons.Contains);
            var scoringWeights = RecipeRuntimeSettings.GetCandidateScoringWeights(packRecipe);
            var singleSeasonBoost = coveredSeasons.Count == 1 ? scoringWeights.SingleSeasonBoost : 0;
            var extrasPriorityBoost = RecipeRuntimeSettings.GetPackExtrasPriorityScoreBoost(packRecipe, result.FileName);
            var qualityScore = TorrentQuality.GetRank(parsed.Quality);
            var audioScore = !string.IsNullOrWhiteSpace(show.PreferredAudioCodec) &&
                             result.FileName.Contains(show.PreferredAudioCodec, StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;

            candidates.Add(new SeasonPackCandidate
            {
                ShowId = show.Id,
                OwnerSeasonNumber = selectedSeasons.First(season => coveredSeasons.Contains(season)),
                FileName = result.FileName,
                FileUrl = result.FileUrl,
                PluginName = result.EngineName,
                FileSize = result.FileSize,
                Seeders = result.Seeders,
                Leechers = result.Leechers,
                QualityLabel = TorrentQuality.Detect(result.FileName),
                AudioCodecLabel = DetectAudioCodec(result.FileName),
                CoveredSeasons = coveredSeasons,
                ContentProfile = contentProfile,
                TotalScore = TorrentQuality.CalculateCandidateScore(
                    qualityScore,
                    audioScore,
                    result.Seeders,
                    matchingSeasonCount * scoringWeights.SeasonMatchScorePerSeason,
                    singleSeasonBoost + extrasPriorityBoost,
                    scoringWeights),
                Warning = contentProfile.BuildWarningText()
            });
        }

        return candidates
            .GroupBy(candidate => candidate.FileUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.TotalScore).First())
            .OrderByDescending(candidate => candidate.QualityLabel is { Length: > 0 } quality ? TorrentQuality.GetRank(quality) : 0)
            .ThenByDescending(candidate => candidate.Seeders)
            .ThenByDescending(candidate => candidate.TotalScore)
            .Take(maxCandidates)
            .ToList();
    }

    private static string? GetPackRejectReason(
        TrackedShow show,
        IReadOnlyList<int> selectedSeasons,
        TorrentSearchResult result,
        TorrentCandidateParseResult parsed,
        IReadOnlyList<string> selectedQualities,
        IReadOnlyList<int>? coveredSeasonsOverride = null)
    {
        var coveredSeasons = coveredSeasonsOverride ?? parsed.CoveredSeasons;
        if (!result.CanAdd)
        {
            return $"not addable link type '{result.LinkType}'";
        }

        if (LooksLikePluginError(result.FileName))
        {
            return "search plugin error row";
        }

        if (coveredSeasons.Count == 0)
        {
            return "no explicit season coverage";
        }

        if (parsed.ExplicitYear is not null && show.FirstAirYear is not null && parsed.ExplicitYear != show.FirstAirYear)
        {
            return $"explicit year mismatch {parsed.ExplicitYear} != {show.FirstAirYear}";
        }

        if (show.FirstAirYear is not null &&
            parsed.ExplicitYear is null &&
            result.FileName.Contains("20", StringComparison.OrdinalIgnoreCase) &&
            !TorrentCandidateParser.ContainsYearRangeIncluding(result.FileName, show.FirstAirYear.Value))
        {
            // Do not reject date-range names here; explicit mismatches are handled above.
        }

        if (!coveredSeasons.Any(selectedSeasons.Contains))
        {
            return $"does not cover selected seasons {string.Join(",", selectedSeasons)}";
        }

        if (!TorrentQuality.MatchesSelectedQuality(parsed.Quality, selectedQualities))
        {
            return $"does not match selected quality options: {string.Join(", ", selectedQualities)}";
        }

        if (result.Seeders < show.MinimumSeeders)
        {
            return $"seeders below threshold {show.MinimumSeeders}";
        }

        var titleTokens = TorrentCandidateParser.Tokenize(show.Title).Where(token => token.Length > 2).ToList();
        var matched = titleTokens.Count(token => parsed.TitleTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
        return matched >= Math.Min(2, titleTokens.Count) ? null : "does not contain enough show title tokens";
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Kept for potential reuse after sequential search refactor.")]
    private List<EpisodeFetchCandidate> MapEpisodeCandidates(
        TrackedEpisode episode,
        TrackedShow show,
        string query,
        IReadOnlyList<TorrentSearchResult> searchResults,
        IReadOnlyList<string> selectedQualities,
        SearchRecipe? recipe = null)
    {
        var scoringWeights = recipe is not null
            ? RecipeRuntimeSettings.GetCandidateScoringWeights(recipe)
            : CandidateScoringWeights.Default;
        var matchedCandidates = new List<(EpisodeFetchCandidate Candidate, CandidateMatchResult Match)>();
        foreach (var result in searchResults)
        {
            var match = CandidateMatcher.MatchEpisodeCandidate(show, episode, result, selectedQualities, scoringWeights);
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
            .OrderByDescending(entry => entry.Match.QualityScore)
            .ThenByDescending(entry => entry.Match.AudioScore)
            .ThenByDescending(entry => entry.Candidate.Seeders)
            .ThenByDescending(entry => entry.Match.IdentityScore)
            .ThenByDescending(entry => entry.Match.EpisodeScore)
            .Take(RecipeRuntimeSettings.GetMaxCandidatesPerFetch(
                _recipeService.GetRecipeOrDefault(show.RecipeId, MediaKind.TvEpisode),
                _settingsService.Current.AutoTorrent))
            .Select(entry => entry.Candidate)
            .ToList();
    }

    private static List<(EpisodeFetchCandidate Candidate, TMatch Match)> DeduplicateEpisodeCandidates<TMatch>(
        List<(EpisodeFetchCandidate Candidate, TMatch Match)> candidates,
        bool fuzzy,
        int sizeToleranceMb)
    {
        var result = candidates
            .GroupBy(entry => entry.Candidate.FileUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(entry => entry.Candidate.Seeders).First())
            .ToList();

        if (!fuzzy)
        {
            return result;
        }

        return result
            .GroupBy(entry => (
                NormalizeFileName(entry.Candidate.FileName),
                SizeBucket(entry.Candidate.FileSize, sizeToleranceMb)))
            .Select(group => group.OrderByDescending(entry => entry.Candidate.Seeders).First())
            .ToList();
    }

    private static string NormalizeFileName(string name)
    {
        name = Regex.Replace(name, @"\([^)]*\)|\[[^\]]*\]", " ");
        return Regex.Replace(name.ToLowerInvariant().Trim(), @"\s+", " ");
    }

    private static long SizeBucket(long bytes, int toleranceMb)
    {
        if (toleranceMb <= 0)
        {
            return bytes;
        }

        var bucketBytes = 1024L * 1024L * toleranceMb;
        return bytes / bucketBytes;
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
            QualityLabel = TorrentQuality.Detect(result.FileName),
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
        int qualityScore,
        int totalScore)
    {
        var candidate = ToCandidate(0, result, qualityScore, totalScore);
        candidate.MovieId = movieId;
        return candidate;
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

    private static string DetectAudioCodec(string fileName)
    {
        string[] codecs = ["TrueHD", "Atmos", "DTS-HD", "DTS", "DDP", "DD+", "AAC", "AC3", "FLAC", "Opus"];
        return codecs.FirstOrDefault(codec => fileName.Contains(codec, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }
}
