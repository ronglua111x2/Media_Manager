using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class FetchJobService : IFetchJobService
{
    private const int SearchCapacityRetryCount = 3;
    private const int SearchCapacityRetryDelayMilliseconds = 2000;
    private const int SearchPollDelayMilliseconds = 1000;

    private readonly IDatabaseService _databaseService;
    private readonly ISettingsService _settingsService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IQbittorrentSearchPluginService _searchPluginService;
    private readonly IRecipeService _recipeService;
    private readonly ISearchPlanBuilder _searchPlanBuilder;
    private readonly ICandidateEvaluationService _candidateEvaluationService;
    private readonly ISearchTitleResolver _titleResolver;
    private readonly ShowSearchSnapshotService _snapshotService;
    private readonly ITorrentBlacklistService _blacklistService;
    private readonly IOperationProgressService _progressService;
    private readonly IAppLogger _logger;
    private readonly Dictionary<long, IReadOnlyList<EpisodeFetchCandidate>> _candidatesByEpisodeId = [];
    private readonly Dictionary<long, int> _searchRowsByEpisodeId = [];
    private readonly Dictionary<long, IReadOnlyList<EpisodeFetchCandidate>> _candidatesByMovieId = [];
    private readonly Dictionary<(long ShowId, int SeasonNumber), IReadOnlyList<SeasonPackCandidate>> _packCandidatesBySeason = [];
    private readonly object _gate = new();
    private CartCandidateDebugSession? _activeHuntDebugSession;

    public FetchJobService(
        IDatabaseService databaseService,
        ISettingsService settingsService,
        IQbittorrentClient qbittorrentClient,
        IQbittorrentSearchPluginService searchPluginService,
        IRecipeService recipeService,
        ISearchPlanBuilder searchPlanBuilder,
        ICandidateEvaluationService candidateEvaluationService,
        ISearchTitleResolver titleResolver,
        ShowSearchSnapshotService snapshotService,
        ITorrentBlacklistService blacklistService,
        IOperationProgressService progressService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _settingsService = settingsService;
        _qbittorrentClient = qbittorrentClient;
        _searchPluginService = searchPluginService;
        _recipeService = recipeService;
        _searchPlanBuilder = searchPlanBuilder;
        _candidateEvaluationService = candidateEvaluationService;
        _titleResolver = titleResolver;
        _snapshotService = snapshotService;
        _blacklistService = blacklistService;
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

    public int GetSearchRowCount(long episodeId)
    {
        lock (_gate)
        {
            return _searchRowsByEpisodeId.TryGetValue(episodeId, out var rows) ? rows : 0;
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

    public CartCandidateDebugSession? TakeHuntDebugSession()
    {
        var session = _activeHuntDebugSession;
        _activeHuntDebugSession = null;
        return session;
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<EpisodeFetchCandidate>>> FetchEpisodeCandidatesAsync(
        long showId,
        IReadOnlyList<long> episodeIds,
        string? recipeId = null,
        Action<long, string>? statusChanged = null,
        CancellationToken cancellationToken = default,
        EpisodeFetchOptions? options = null,
        bool finalizeDebugLog = true)
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

        _progressService.Start("Search: preparing…", 0);
        _activeHuntDebugSession = HuntCandidateDebugWriter.TryCreateSession(
            recipe,
            _settingsService,
            _logger,
            options?.Overrides);
        try
        {
            return useSnapshot
                ? await FetchEpisodeCandidatesSnapshotAsync(show, targetEpisodes, recipe, statusChanged, cancellationToken, options?.Overrides)
                : await FetchEpisodeCandidatesParallelAsync(show, targetEpisodes, recipe, statusChanged, cancellationToken, options?.MaxParallelWorkers, options?.Overrides);
        }
        finally
        {
            if (_activeHuntDebugSession is not null && finalizeDebugLog)
            {
                HuntCandidateDebugWriter.LogSessionPath(_logger, _activeHuntDebugSession);
                _activeHuntDebugSession = null;
            }

            _progressService.Finish("Idle");
        }
    }

    private void TryWriteCandidateDebug(
        SearchRecipe recipe,
        string targetTitle,
        MediaKind targetKind,
        IReadOnlyList<string> queries,
        IReadOnlyList<TorrentSearchResult> searchResults,
        IReadOnlyList<RecipeCandidateResult> evaluated,
        int timeoutSeconds,
        string? huntMatchTitle = null,
        string? huntMatchEpisodeLabel = null,
        IReadOnlyList<EpisodeFetchCandidate>? huntMatched = null)
    {
        if (_activeHuntDebugSession is null)
        {
            return;
        }

        HuntCandidateDebugWriter.WriteRecipeEvaluation(
            _activeHuntDebugSession,
            recipe,
            targetTitle,
            targetKind,
            queries,
            searchResults,
            evaluated,
            timeoutSeconds);
        if (huntMatchTitle is not null && huntMatchEpisodeLabel is not null && huntMatched is not null)
        {
            HuntCandidateDebugWriter.WriteHuntMatch(
                _activeHuntDebugSession,
                recipe,
                huntMatchTitle,
                huntMatchEpisodeLabel,
                searchResults.Count,
                huntMatched);
        }
    }

    public async Task FetchSeasonPacksAsync(
        long showId,
        IReadOnlyList<int> seasonNumbers,
        CancellationToken cancellationToken = default,
        RecipeExecutionOverrides? overrides = null,
        string? recipeId = null)
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

        _progressService.Start("Search: preparing…", 0);
        try
        {
            var packRecipe = _recipeService.GetRecipeOrDefault(recipeId ?? show.PackRecipeId, MediaKind.TvSeasonPack);
            var packDebugSession = HuntCandidateDebugWriter.TryCreateSession(
                packRecipe,
                _settingsService,
                _logger,
                overrides);
            var snapshotResults = await _snapshotService.CaptureSnapshotAsync(
                show,
                _progressService,
                cancellationToken,
                MediaKind.TvSeasonPack,
                packRecipe.RecipeId);
            _progressService.Report(0, "Filtering results...");
            var packSummary = await MapSeasonPackCandidatesAsync(
                show,
                selectedSeasons,
                snapshotResults,
                packRecipe,
                overrides,
                cancellationToken);
            var candidates = packSummary.Candidates;
            lock (_gate)
            {
                foreach (var seasonNumber in selectedSeasons)
                {
                    _packCandidatesBySeason[(showId, seasonNumber)] = candidates
                        .Where(candidate => candidate.CoveredSeasons.Contains(seasonNumber))
                        .ToList();
                }
            }

            if (packDebugSession is not null)
            {
                var packQueries = _searchPlanBuilder.BuildShowSnapshotQueries(packRecipe, show);
                HuntCandidateDebugWriter.WriteRecipeEvaluation(
                    packDebugSession,
                    packRecipe,
                    show.DisplayTitle,
                    MediaKind.TvSeasonPack,
                    packQueries,
                    snapshotResults,
                    packSummary.Evaluated,
                    RecipeRuntimeSettings.GetSnapshotTimeoutSeconds(packRecipe, _settingsService.Current.AutoTorrent));
                HuntCandidateDebugWriter.LogSessionPath(_logger, packDebugSession);
            }

            _logger.Info($"Fetched season pack candidates for {show.DisplayTitle}. Seasons={string.Join(",", selectedSeasons)}, Candidates={candidates.Count}.", LogTarget.All);
        }
        finally
        {
            _progressService.Finish("Idle");
        }
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
                _searchRowsByEpisodeId[episode.Id] = 0;
            }
        }
    }

    private async Task<IReadOnlyDictionary<long, IReadOnlyList<EpisodeFetchCandidate>>> FetchEpisodeCandidatesParallelAsync(
        TrackedShow show,
        IReadOnlyList<TrackedEpisode> targetEpisodes,
        SearchRecipe recipe,
        Action<long, string>? statusChanged,
        CancellationToken cancellationToken,
        int? maxParallelWorkersOverride = null,
        RecipeExecutionOverrides? overrides = null)
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
                var searchSummary = await SearchEpisodeCandidatesSequentialAsync(recipe, episode, show, queries, cancellationToken, overrides);
                lock (_gate)
                {
                    _candidatesByEpisodeId[episode.Id] = searchSummary.Candidates;
                    _searchRowsByEpisodeId[episode.Id] = searchSummary.SearchResults.Count;
                }

                lock (resultGate)
                {
                    results[episode.Id] = searchSummary.Candidates;
                }

                var detail = searchSummary.Candidates.Count == 0
                    ? $"No recipe match found for {label}."
                    : $"Matched {searchSummary.Candidates.Count} candidate(s) for {label}.";
                statusChanged?.Invoke(episode.Id, detail);
                _logger.Info(
                    $"Cart parallel search finished {label}. searchRows={searchSummary.SearchResults.Count}, recipeMatched={searchSummary.Candidates.Count}.",
                    LogTarget.All);
                TryWriteCandidateDebug(
                    recipe,
                    show.DisplayTitle,
                    MediaKind.TvEpisode,
                    queries,
                    searchSummary.SearchResults,
                    searchSummary.Evaluated,
                    RecipeRuntimeSettings.GetParallelSearchTimeoutSeconds(recipe, _settingsService.Current.AutoTorrent),
                    show.DisplayTitle,
                    label,
                    searchSummary.Candidates);
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
        CancellationToken cancellationToken,
        RecipeExecutionOverrides? overrides)
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

        var snapshotResults = await _snapshotService.CaptureSnapshotAsync(
            show,
            _progressService,
            cancellationToken,
            MediaKind.TvEpisode,
            recipe.RecipeId);
        _progressService.Report(0, "Filtering results...");
        var usesAnimeAbsolute = RecipeRuntimeSettings.UsesAnimeAbsoluteEpisodeNumbering(recipe);
        var snapshotCandidates = snapshotResults
            .Select(result => new SnapshotCandidate
            {
                Result = result,
                Parsed = TorrentCandidateParser.Parse(result.FileName, usesAnimeAbsolute)
            })
            .ToList();
        var matcher = new SnapshotCandidateMatcher();
        var titleVariants = _titleResolver.Resolve(
            _titleResolver.CreateRequest(recipe, show.Title, show.GetSearchableAlternativeTitles()));
        var snapshotQueries = _searchPlanBuilder.BuildShowSnapshotQueries(recipe, show);
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
                var mapped = await MapSnapshotCandidatesAsync(
                    show,
                    episode,
                    snapshotCandidates,
                    matcher,
                    titleVariants,
                    cartJob,
                    cancellationToken,
                    recipe,
                    overrides);

                lock (_gate)
                {
                    _candidatesByEpisodeId[episode.Id] = mapped.Candidates;
                    _searchRowsByEpisodeId[episode.Id] = snapshotCandidates.Count;
                }

                lock (resultGate)
                {
                    results[episode.Id] = mapped.Candidates;
                }

                var detail = mapped.Candidates.Count == 0
                    ? $"No snapshot match found for {label}."
                    : $"Matched {mapped.Candidates.Count} candidate(s) for {label}.";
                statusChanged?.Invoke(episode.Id, detail);
                TryWriteCandidateDebug(
                    recipe,
                    show.DisplayTitle,
                    MediaKind.TvEpisode,
                    snapshotQueries,
                    snapshotResults,
                    mapped.Evaluated,
                    RecipeRuntimeSettings.GetSnapshotTimeoutSeconds(recipe, _settingsService.Current.AutoTorrent),
                    show.DisplayTitle,
                    label,
                    mapped.Candidates);
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
                    results.AddRange(await _qbittorrentClient.SearchAsync(new TorrentSearchRequest
                    {
                        Query = query,
                        TimeoutSeconds = _settingsService.Current.AutoTorrent.ParallelSearchTimeoutSeconds
                    }, cancellationToken));
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
        public EpisodeSearchSummary(
            IReadOnlyList<EpisodeFetchCandidate> candidates,
            IReadOnlyList<TorrentSearchResult> searchResults,
            IReadOnlyList<RecipeCandidateResult> evaluated)
        {
            Candidates = candidates;
            SearchResults = searchResults;
            Evaluated = evaluated;
        }

        public IReadOnlyList<EpisodeFetchCandidate> Candidates { get; }

        public IReadOnlyList<TorrentSearchResult> SearchResults { get; }

        public IReadOnlyList<RecipeCandidateResult> Evaluated { get; }
    }

    private sealed class SnapshotMapSummary
    {
        public required IReadOnlyList<EpisodeFetchCandidate> Candidates { get; init; }

        public required IReadOnlyList<RecipeCandidateResult> Evaluated { get; init; }
    }

    private sealed class PackMapSummary
    {
        public required IReadOnlyList<SeasonPackCandidate> Candidates { get; init; }

        public required IReadOnlyList<RecipeCandidateResult> Evaluated { get; init; }
    }

    private async Task<EpisodeSearchSummary> SearchEpisodeCandidatesSequentialAsync(
        SearchRecipe recipe,
        TrackedEpisode episode,
        TrackedShow show,
        IReadOnlyList<string> queries,
        CancellationToken cancellationToken,
        RecipeExecutionOverrides? overrides)
    {
        recipe = RecipeRuntimeSettings.WithCartOverrides(recipe, overrides);
        var maxCandidates = RecipeRuntimeSettings.GetMaxCandidatesPerFetch(recipe, _settingsService.Current.AutoTorrent, overrides);
        var writeDebug = _activeHuntDebugSession is not null;
        var resultsByUrl = new Dictionary<string, TorrentSearchResult>(StringComparer.OrdinalIgnoreCase);
        var matchedCandidates = new List<(EpisodeFetchCandidate Candidate, RecipeCandidateResult Match)>();
        var evaluated = new List<RecipeCandidateResult>();
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
            var queryResults = await SearchSingleQueryAsync(recipe, query, cancellationToken);
            completedQueries++;
            _progressService.Report(completedQueries, $"Search: Query {completedQueries}/{totalQueries}");
            _logger.Info(
                $"Episode search query succeeded {completedQueries}/{totalQueries}. Remaining={totalQueries - completedQueries}. Query='{query}'. Results={queryResults.Count}.",
                LogTarget.All);

            foreach (var result in queryResults)
            {
                if (!string.IsNullOrWhiteSpace(result.FileUrl))
                {
                    resultsByUrl[result.FileUrl] = result;
                }

                if (!writeDebug && matchedCandidates.Count >= maxCandidates)
                {
                    continue;
                }

                var match = _candidateEvaluationService.EvaluateEpisode(recipe, show, episode, result);
                if (writeDebug)
                {
                    evaluated.Add(match);
                }

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

                if (IsBlacklistedListing(show.Id, result, query))
                {
                    continue;
                }

                if (matchedCandidates.Count >= maxCandidates)
                {
                    continue;
                }

                var candidate = ToCandidate(episode.Id, result, match.QualityScore, match.TotalScore);
                matchedCandidates.Add((candidate, match));
            }

            if (!writeDebug && matchedCandidates.Count >= maxCandidates)
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
            .ThenByDescending(entry => entry.Match.PreferTermsScore)
            .ThenByDescending(entry => entry.Match.SizeScore)
            .ThenByDescending(entry => entry.Candidate.Seeders)
            .ThenByDescending(entry => entry.Match.IdentityScore)
            .ThenByDescending(entry => entry.Match.EpisodeScore)
            .Take(maxCandidates)
            .Select(entry => entry.Candidate)
            .ToList();

        var searchResults = resultsByUrl.Values
            .OrderByDescending(result => result.Seeders)
            .ToList();

        return new EpisodeSearchSummary(candidates, searchResults, evaluated);
    }

    private async Task<IReadOnlyList<TorrentSearchResult>> SearchSingleQueryAsync(
        SearchRecipe recipe,
        string query,
        CancellationToken cancellationToken)
    {
        var searchSource = recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.SearchSource && module.IsEnabled);
        var requestLimit = searchSource?.ResultLimit is > 0 ? searchSource.ResultLimit : 100;
        var savedPlugins = string.IsNullOrWhiteSpace(searchSource?.Plugins) ? "enabled" : searchSource!.Plugins;
        var category = string.IsNullOrWhiteSpace(searchSource?.Category) ? "all" : searchSource!.Category;
        var livePlugins = await _searchPluginService.GetPluginsAsync(cancellationToken);
        var resolved = _searchPluginService.ResolveForSearch(savedPlugins, livePlugins, _logger);
        if (resolved.SkipSearch)
        {
            _logger.Warning($"Search skipped for query '{query}': no valid enabled plugins remain.", LogTarget.All);
            return [];
        }

        var plugins = resolved.PluginsForApi;
        var requestedEngineNames = resolved.RequestedNames;
        var timeoutSeconds = RecipeRuntimeSettings.GetParallelSearchTimeoutSeconds(
            recipe,
            _settingsService.Current.AutoTorrent);
        var paginationEnabled = RecipeRuntimeSettings.GetEnableSearchPagination(recipe);
        var paginationPageSize = RecipeRuntimeSettings.GetPaginationPageSize(recipe);
        var paginationMaxPages = RecipeRuntimeSettings.GetPaginationMaxPagesTvParallel(recipe);
        var paginationMaxTotalResults = RecipeRuntimeSettings.GetPaginationMaxTotalResults(recipe);
        var paginationIdleTimeoutSeconds = RecipeRuntimeSettings.GetPaginationIdleTimeoutSecondsTvParallel(recipe);
        var searchIdleTimeoutSeconds = RecipeRuntimeSettings.GetSearchIdleTimeoutSecondsTvParallel(recipe);
        for (var attempt = 1; attempt <= SearchCapacityRetryCount; attempt++)
        {
            try
            {
                return paginationEnabled
                    ? await SearchQueryWithPaginationAsync(
                        query,
                        plugins,
                        category,
                        timeoutSeconds,
                        requestLimit,
                        paginationPageSize,
                        paginationMaxPages,
                        paginationMaxTotalResults,
                        paginationIdleTimeoutSeconds,
                        requestedEngineNames,
                        cancellationToken)
                    : await _qbittorrentClient.SearchAsync(new TorrentSearchRequest
                    {
                        Query = query,
                        Plugins = plugins,
                        Category = category,
                        Limit = requestLimit,
                        IdleTimeoutSeconds = searchIdleTimeoutSeconds,
                        TimeoutSeconds = timeoutSeconds,
                        RequestedEngineNames = requestedEngineNames
                    }, cancellationToken);
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

    private async Task<IReadOnlyList<TorrentSearchResult>> SearchQueryWithPaginationAsync(
        string query,
        string plugins,
        string category,
        int timeoutSeconds,
        int requestLimit,
        int pageSize,
        int maxPages,
        int maxTotalResults,
        int idleTimeoutSeconds,
        IReadOnlyList<string> requestedEngineNames,
        CancellationToken cancellationToken)
    {
        var effectivePageSize = Math.Clamp(pageSize, RecipeRuntimeSettings.MinPaginationPageSize, RecipeRuntimeSettings.MaxPaginationPageSize);
        var effectiveMaxPages = Math.Clamp(maxPages, RecipeRuntimeSettings.MinPaginationMaxPages, RecipeRuntimeSettings.MaxPaginationMaxPages);
        var effectiveMaxTotal = Math.Clamp(
            maxTotalResults,
            RecipeRuntimeSettings.MinPaginationMaxTotalResults,
            RecipeRuntimeSettings.MaxPaginationMaxTotalResults);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 10, 300));
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var idleTimeout = idleTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(Math.Clamp(idleTimeoutSeconds, RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds, RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds))
            : (TimeSpan?)null;
        var latestStatus = "Running";
        var pagesFetched = 0;
        var rawRows = 0;
        var mergedByUrl = new Dictionary<string, TorrentSearchResult>(StringComparer.OrdinalIgnoreCase);
        var endedBy = "timeout";
        var lastMergedCount = 0;
        var idleDeadline = idleTimeout is null ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.Add(idleTimeout.Value);
        int? searchId = null;

        try
        {
            searchId = await _qbittorrentClient.StartSearchAsync(new TorrentSearchRequest
            {
                Query = query,
                Plugins = plugins,
                Category = category
            }, cancellationToken);
            while (DateTimeOffset.UtcNow < deadline)
            {
                for (var pageIndex = 0; pageIndex < effectiveMaxPages && mergedByUrl.Count < effectiveMaxTotal; pageIndex++)
                {
                    var offset = pageIndex * effectivePageSize;
                    var response = await _qbittorrentClient.GetSearchResultsAsync(
                        searchId.Value,
                        effectivePageSize,
                        offset,
                        cancellationToken);
                    latestStatus = response.Status;
                    pagesFetched++;
                    rawRows += response.Results.Count;
                    foreach (var result in response.Results)
                    {
                        if (string.IsNullOrWhiteSpace(result.FileUrl))
                        {
                            continue;
                        }

                        if (!mergedByUrl.TryGetValue(result.FileUrl, out var existing) ||
                            result.Seeders > existing.Seeders)
                        {
                            mergedByUrl[result.FileUrl] = result;
                        }
                    }

                    if (response.Results.Count < effectivePageSize)
                    {
                        break;
                    }
                }

                if (mergedByUrl.Count > lastMergedCount)
                {
                    lastMergedCount = mergedByUrl.Count;
                    if (idleTimeout is not null)
                    {
                        idleDeadline = DateTimeOffset.UtcNow.Add(idleTimeout.Value);
                    }
                }
                else if (idleTimeout is not null && DateTimeOffset.UtcNow >= idleDeadline)
                {
                    endedBy = "idle-timeout";
                    break;
                }

                if (!string.Equals(latestStatus, "Running", StringComparison.OrdinalIgnoreCase))
                {
                    endedBy = "status-finished";
                    break;
                }

                if (mergedByUrl.Count >= effectiveMaxTotal)
                {
                    endedBy = "max-results";
                    break;
                }

                await Task.Delay(SearchPollDelayMilliseconds, cancellationToken);
            }
        }
        finally
        {
            if (searchId is not null)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    await _qbittorrentClient.StopSearchAsync(searchId.Value, CancellationToken.None);
                }

                await _qbittorrentClient.DeleteSearchAsync(searchId.Value, CancellationToken.None);
            }
        }

        var merged = mergedByUrl.Values
            .OrderByDescending(result => result.Seeders)
            .ThenBy(result => result.FileSize)
            .Take(effectiveMaxTotal)
            .ToList();
        if (endedBy == "timeout" && DateTimeOffset.UtcNow < deadline)
        {
            endedBy = "status-finished";
        }

        _logger.Info(
            $"Fetch query pagination query='{query}' pages={pagesFetched} rawRows={rawRows} mergedRows={merged.Count} status='{latestStatus}' pageSize={effectivePageSize} maxPages={effectiveMaxPages} cap={effectiveMaxTotal} idleSeconds={idleTimeoutSeconds} endedBy='{HuntLogFormatter.FormatEndedBy(endedBy, merged.Count)}' engines=[{SearchEngineDiagnostics.BuildEngineSummaryIncludingEmpty(requestedEngineNames, merged)}].",
            LogTarget.All);
        SearchEngineDiagnostics.LogEmptyEngines(_logger, requestedEngineNames, merged, query);
        return merged;
    }

    private bool IsBlacklistedListing(long mediaId, TorrentSearchResult result, string? queryForLog = null)
    {
        if (!_blacklistService.IsBlacklisted(mediaId, result.FileUrl))
        {
            return false;
        }

        var queryLabel = string.IsNullOrWhiteSpace(queryForLog) ? result.FileName : queryForLog;
        _logger.Debug(
            $"Rejected search candidate for '{queryLabel}'. Reason='{CandidateRejectReason.Blacklisted}: listing URL or infohash blacklisted for media {mediaId}', Engine='{result.EngineName}', Name='{result.FileName}', Url='{result.FileUrl}'.",
            LogTarget.File);
        return true;
    }

    private async Task<string?> GetEpisodeMetadataRejectReasonAsync(
        TrackedShow show,
        TrackedEpisode episode,
        TorrentSearchResult result,
        TorrentCandidateParseResult parsed,
        int identityScore,
        CancellationToken cancellationToken,
        SearchRecipe? recipeOverride = null)
    {
        var recipe = recipeOverride ?? _recipeService.GetRecipeOrDefault(show.RecipeId, MediaKind.TvEpisode);
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

    private async Task<SnapshotMapSummary> MapSnapshotCandidatesAsync(
        TrackedShow show,
        TrackedEpisode episode,
        IReadOnlyList<SnapshotCandidate> snapshotCandidates,
        SnapshotCandidateMatcher matcher,
        IReadOnlyList<string> titleVariants,
        FetchJob job,
        CancellationToken cancellationToken,
        SearchRecipe? recipeOverride = null,
        RecipeExecutionOverrides? overrides = null)
    {
        var matchedCandidates = new List<(EpisodeFetchCandidate Candidate, SnapshotMatchResult Match)>();
        var evaluated = new List<RecipeCandidateResult>();
        var recipe = RecipeRuntimeSettings.WithCartOverrides(
            recipeOverride ?? _recipeService.GetRecipeOrDefault(show.RecipeId, MediaKind.TvEpisode),
            overrides);
        var scoringWeights = RecipeRuntimeSettings.GetCandidateScoringWeights(recipe);
        foreach (var candidate in snapshotCandidates)
        {
            var match = matcher.Match(show, episode, candidate, recipe, titleVariants, scoringWeights);
            var recipeResult = match.ToRecipeCandidateResult(candidate.Result);
            if (!match.IsAccepted)
            {
                if (match.RejectReason?.Contains("plugin error", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.Warning(
                        $"Snapshot search plugin error row for '{show.DisplayTitle} {episode.SeasonNumber:00}x{episode.EpisodeNumber:00}'. Engine='{candidate.Result.EngineName}', Name='{candidate.Result.FileName}'.",
                        LogTarget.All);
                }

                evaluated.Add(recipeResult);
                continue;
            }

            var metadataRejectReason = await GetEpisodeMetadataRejectReasonAsync(
                show,
                episode,
                candidate.Result,
                candidate.Parsed,
                match.IdentityScore,
                cancellationToken,
                recipe);
            if (metadataRejectReason is not null)
            {
                evaluated.Add(RejectedRecipeResult(candidate.Result, CandidateRejectReason.YearMismatch, metadataRejectReason));
                continue;
            }

            if (IsBlacklistedListing(show.Id, candidate.Result))
            {
                evaluated.Add(RejectedRecipeResult(candidate.Result, CandidateRejectReason.Blacklisted, "listing URL or infohash blacklisted"));
                continue;
            }

            evaluated.Add(recipeResult);
            var episodeCandidate = ToCandidate(episode.Id, candidate.Result, match.QualityScore, match.TotalScore);
            matchedCandidates.Add((episodeCandidate, match));
        }

        var maxCandidates = RecipeRuntimeSettings.GetMaxCandidatesPerFetch(recipe, _settingsService.Current.AutoTorrent, overrides);
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
            .ThenByDescending(entry => entry.Match.PreferTermsScore)
            .ThenByDescending(entry => entry.Match.SizeScore)
            .ThenByDescending(entry => entry.Candidate.Seeders)
            .ThenByDescending(entry => entry.Match.IdentityScore)
            .ThenByDescending(entry => entry.Match.EpisodeScore)
            .Take(maxCandidates)
            .Select(entry => entry.Candidate)
            .ToList();

        _logger.Info(
            $"Snapshot matching complete for {show.DisplayTitle} {episode.SeasonNumber:00}x{episode.EpisodeNumber:00}. recipeMatched={finalCandidates.Count} (from snapshotSize={snapshotCandidates.Count}), JobId={job.Id}.",
            LogTarget.All);

        return new SnapshotMapSummary
        {
            Candidates = finalCandidates,
            Evaluated = evaluated
        };
    }

    private async Task<PackMapSummary> MapSeasonPackCandidatesAsync(
        TrackedShow show,
        IReadOnlyList<int> selectedSeasons,
        IReadOnlyList<TorrentSearchResult> snapshotResults,
        SearchRecipe packRecipe,
        RecipeExecutionOverrides? overrides,
        CancellationToken cancellationToken)
    {
        packRecipe = RecipeRuntimeSettings.WithCartOverrides(packRecipe, overrides);
        var maxCandidates = RecipeRuntimeSettings.GetMaxCandidatesPerFetch(
            packRecipe,
            _settingsService.Current.AutoTorrent,
            overrides);
        var candidates = new List<SeasonPackCandidate>();
        var evaluated = new List<RecipeCandidateResult>();
        foreach (var result in snapshotResults)
        {
            var parsed = TorrentCandidateParser.Parse(result.FileName);
            var reject = GetPackRejectReason(show, selectedSeasons, result, parsed, packRecipe);
            var rejectReason = PackRejectDetail(reject);
            var coveredSeasons = parsed.CoveredSeasons.ToList();
            TorrentMetadataProbeResult? probe = null;

            if (rejectReason is SeasonPackIdentity.NoExplicitSeasonCoverage && ShouldProbePackCandidate(packRecipe, result, parsed))
            {
                probe = await _qbittorrentClient.ProbeTorrentMetadataAsync(result, cancellationToken);
                var probeRejectReason = GetProbeYearRejectReason(show, probe);
                if (probeRejectReason is not null)
                {
                    reject = (CandidateRejectReason.YearMismatch, probeRejectReason);
                    rejectReason = probeRejectReason;
                }
                else
                {
                    var probedSeasons = InferCoveredSeasonsFromProbe(probe).ToList();
                    if (probedSeasons.Count > 0)
                    {
                        coveredSeasons = probedSeasons;
                        reject = GetPackRejectReason(show, selectedSeasons, result, parsed, packRecipe, coveredSeasons);
                        rejectReason = PackRejectDetail(reject);
                        _logger.Debug(
                            $"Pack metadata probe inferred seasons for '{show.DisplayTitle}'. Seasons={string.Join(",", coveredSeasons)}, Name='{result.FileName}'.",
                            LogTarget.File | LogTarget.Console);
                    }
                }
            }
            else if (rejectReason is null && ShouldProbePackCandidate(packRecipe, result, parsed))
            {
                probe = await _qbittorrentClient.ProbeTorrentMetadataAsync(result, cancellationToken);
                var probeYear = GetProbeYearRejectReason(show, probe);
                rejectReason = probeYear;
                if (probeYear is not null)
                {
                    reject = (CandidateRejectReason.YearMismatch, probeYear);
                }

                var probedSeasons = InferCoveredSeasonsFromProbe(probe).ToList();
                if (rejectReason is null && probedSeasons.Count > 0 && !probedSeasons.Any(selectedSeasons.Contains))
                {
                    rejectReason = $"metadata files do not cover selected seasons {string.Join(",", selectedSeasons)}";
                    reject = (CandidateRejectReason.EpisodeMismatch, rejectReason);
                }
            }

            if (rejectReason is not null)
            {
                _logger.Debug($"Rejected pack candidate for '{show.DisplayTitle}'. Reason='{rejectReason}', Name='{result.FileName}', Url='{result.FileUrl}'.", LogTarget.File | LogTarget.Console);
                evaluated.Add(RejectedRecipeResult(result, reject.Reason, rejectReason));
                continue;
            }

            if (IsBlacklistedListing(show.Id, result, show.DisplayTitle))
            {
                evaluated.Add(RejectedRecipeResult(result, CandidateRejectReason.Blacklisted, "listing URL or infohash blacklisted"));
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
            var packFilter = RecipeCandidateFilter.GetFilterModule(packRecipe);
            var scoringWeights = RecipeRuntimeSettings.GetCandidateScoringWeights(packRecipe);
            var singleSeasonBoost = coveredSeasons.Count == 1 ? scoringWeights.SingleSeasonBoost : 0;
            var extrasPriorityBoost = RecipeRuntimeSettings.GetPackExtrasPriorityScoreBoost(packRecipe, result.FileName);
            var qualityScore = TorrentQuality.GetRank(parsed.Quality);
            var audioScore = PreferredTermMatcher.CountMatches(result.FileName, packFilter?.PreferredAudioCodec);
            var preferTermsScore = PreferredTermMatcher.CountMatches(
                result.FileName,
                GetPreferTerms(packRecipe));
            var sizeScore = TorrentQualityScoring.CalculateSizeScore(
                result.FileSize,
                packFilter?.MinimumSizeBytes,
                packFilter?.MaximumSizeBytes,
                scoringWeights);
            var engineRankScore = RecipeRuntimeSettings.ResolveEngineRankScore(result.EngineName, packFilter);

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
                TotalScore = TorrentQualityScoring.CalculateCandidateScore(
                    qualityScore,
                    audioScore,
                    result.Seeders,
                    matchingSeasonCount * scoringWeights.SeasonMatchScorePerSeason,
                    singleSeasonBoost + extrasPriorityBoost,
                    scoringWeights,
                    preferTermsScore,
                    sizeScore,
                    engineRankScore),
                Warning = contentProfile.BuildWarningText()
            });
            evaluated.Add(new RecipeCandidateResult
            {
                SearchResult = result,
                QualityScore = qualityScore,
                AudioScore = audioScore,
                PreferTermsScore = preferTermsScore,
                SizeScore = sizeScore,
                TotalScore = candidates[^1].TotalScore
            });
        }

        var finalCandidates = candidates
            .GroupBy(candidate => candidate.FileUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.TotalScore).First())
            .OrderByDescending(candidate => candidate.QualityLabel is { Length: > 0 } quality ? TorrentQuality.GetRank(quality) : 0)
            .ThenByDescending(candidate => candidate.Seeders)
            .ThenByDescending(candidate => candidate.TotalScore)
            .Take(maxCandidates)
            .ToList();

        return new PackMapSummary
        {
            Candidates = finalCandidates,
            Evaluated = evaluated
        };
    }

    private static (CandidateRejectReason Reason, string Detail) GetPackRejectReason(
        TrackedShow show,
        IReadOnlyList<int> selectedSeasons,
        TorrentSearchResult result,
        TorrentCandidateParseResult parsed,
        SearchRecipe packRecipe,
        IReadOnlyList<int>? coveredSeasonsOverride = null)
    {
        return SeasonPackIdentity.GetRejectReason(
            show,
            selectedSeasons,
            result,
            parsed,
            packRecipe,
            coveredSeasonsOverride);
    }

    private static string? PackRejectDetail((CandidateRejectReason Reason, string Detail) reject)
    {
        return reject.Reason == CandidateRejectReason.None ? null : reject.Detail;
    }

    private static RecipeCandidateResult RejectedRecipeResult(
        TorrentSearchResult result,
        CandidateRejectReason reason,
        string detail)
    {
        return new RecipeCandidateResult
        {
            SearchResult = result,
            RejectReason = reason,
            RejectDetail = detail
        };
    }

    private static IReadOnlyList<string> GetPreferTerms(SearchRecipe? recipe)
    {
        if (recipe is null)
        {
            return [];
        }

        var filter = recipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.CandidateFilter && module.IsEnabled);
        return filter?.PreferTerms ?? [];
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

    private static string DetectAudioCodec(string fileName)
    {
        string[] codecs = ["TrueHD", "Atmos", "DTS-HD", "DTS", "DDP", "DD+", "AAC", "AC3", "FLAC", "Opus"];
        return codecs.FirstOrDefault(codec => fileName.Contains(codec, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }
}
