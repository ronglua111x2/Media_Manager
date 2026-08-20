using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class AutomationFlowService : IAutomationFlowService
{
    private const int SearchPollDelayMilliseconds = 1000;

    private readonly IDatabaseService _databaseService;
    private readonly IRecipeService _recipeService;
    private readonly ISearchPlanBuilder _searchPlanBuilder;
    private readonly ICandidateEvaluationService _candidateEvaluationService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IQbittorrentSearchPluginService _searchPluginService;
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly ISettingsService _settingsService;
    private readonly IOperationProgressService _progressService;
    private readonly IAppLogger _logger;

    public AutomationFlowService(
        IDatabaseService databaseService,
        IRecipeService recipeService,
        ISearchPlanBuilder searchPlanBuilder,
        ICandidateEvaluationService candidateEvaluationService,
        IQbittorrentClient qbittorrentClient,
        IQbittorrentSearchPluginService searchPluginService,
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        ISettingsService settingsService,
        IOperationProgressService progressService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _recipeService = recipeService;
        _searchPlanBuilder = searchPlanBuilder;
        _candidateEvaluationService = candidateEvaluationService;
        _qbittorrentClient = qbittorrentClient;
        _searchPluginService = searchPluginService;
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _settingsService = settingsService;
        _progressService = progressService;
        _logger = logger;
    }

    public async Task<RecipeDryRunResult> DryRunAsync(RecipeRunRequest request, CancellationToken cancellationToken = default)
    {
        return request.TargetKind == MediaKind.Movie
            ? await DryRunMovieAsync(request, cancellationToken)
            : await DryRunEpisodeAsync(request, cancellationToken);
    }

    public async Task<RecipeDryRunResult> RunNowAsync(RecipeRunRequest request, CancellationToken cancellationToken = default)
    {
        var result = await DryRunAsync(request, cancellationToken);
        var candidate = result.BestCandidate;
        if (candidate is null)
        {
            return result;
        }

        if (request.TargetKind == MediaKind.Movie)
        {
            var movie = GetMovie(request);
            var added = await _qbittorrentClient.AddTorrentAsync(CreateAddTorrentRequest(MediaKind.Movie, candidate.SearchResult, null), cancellationToken);
            _trackedMovieService.UpdateSelectedCandidate(movie.Id, ToMovieCandidate(movie.Id, candidate));
            _trackedMovieService.UpdateTorrentState(movie.Id, added);
            _logger.Info($"Recipe run added movie torrent for {movie.DisplayTitle}: {added.Name}", LogTarget.All);
            return result;
        }

        var (show, episode) = GetEpisode(request);
        var season = _databaseService.GetTrackedSeasons(show.Id)
            .FirstOrDefault(item => item.SeasonNumber == episode.SeasonNumber);
        var addedTorrent = await _qbittorrentClient.AddTorrentAsync(CreateAddTorrentRequest(MediaKind.TvEpisode, candidate.SearchResult, season?.DownloadFolder), cancellationToken);
        var selectedCandidate = ToEpisodeCandidate(episode.Id, candidate);
        _trackedShowService.UpdateSelectedCandidate(episode.Id, selectedCandidate);
        _trackedShowService.UpdateTorrentState(episode.Id, addedTorrent);
        _logger.Info($"Recipe run added torrent for {show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}: {addedTorrent.Name}", LogTarget.All);
        return result;
    }

    private async Task<RecipeDryRunResult> DryRunEpisodeAsync(RecipeRunRequest request, CancellationToken cancellationToken)
    {
        _progressService.Start("Search: preparing…", 0);
        try
        {
            var (show, episode) = GetEpisode(request);
            var recipe = _recipeService.GetRecipeOrDefault(request.RecipeId ?? show.RecipeId, MediaKind.TvEpisode);
            var queries = _searchPlanBuilder.BuildEpisodeQueries(recipe, show, episode);
            var results = await SearchAsync(recipe, queries, cancellationToken);
            _progressService.Report(0, "Filtering results...");
            var evaluated = results
                .Select(result => _candidateEvaluationService.EvaluateEpisode(recipe, show, episode, result))
                .ToList();
            TryWriteCandidateDebugLog(recipe, show.DisplayTitle, MediaKind.TvEpisode, queries, results, evaluated);
            return BuildDryRunResult(recipe, show.DisplayTitle, queries, evaluated);
        }
        finally
        {
            _progressService.Finish("Idle");
        }
    }

    private async Task<RecipeDryRunResult> DryRunMovieAsync(RecipeRunRequest request, CancellationToken cancellationToken)
    {
        _progressService.Start("Search: preparing…", 0);
        try
        {
            var movie = GetMovie(request);
            var recipe = _recipeService.GetRecipeOrDefault(request.RecipeId ?? movie.RecipeId, MediaKind.Movie);
            var queries = _searchPlanBuilder.BuildMovieQueries(recipe, movie);
            var results = await SearchAsync(recipe, queries, cancellationToken);
            _progressService.Report(0, "Filtering results...");
            var evaluated = results
                .Select(result => _candidateEvaluationService.EvaluateMovie(recipe, movie, result))
                .ToList();
            TryWriteCandidateDebugLog(recipe, movie.DisplayTitle, MediaKind.Movie, queries, results, evaluated);
            return BuildDryRunResult(recipe, movie.DisplayTitle, queries, evaluated);
        }
        finally
        {
            _progressService.Finish("Idle");
        }
    }

    private async Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(
        SearchRecipe recipe,
        IReadOnlyList<string> queries,
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
            _logger.Warning($"Recipe search skipped: no valid enabled plugins remain for recipe '{recipe.Name}'.", LogTarget.All);
            return [];
        }

        var plugins = resolved.PluginsForApi;
        var requestedEngineNames = resolved.RequestedNames;
        var autoTorrent = _settingsService.Current.AutoTorrent;
        var timeoutSeconds = recipe.TargetKind == MediaKind.Movie
            ? RecipeRuntimeSettings.GetMovieSearchTimeoutSeconds(recipe, autoTorrent)
            : RecipeRuntimeSettings.GetParallelSearchTimeoutSeconds(recipe, autoTorrent);
        var parallelSearches = RecipeRuntimeSettings.GetParallelSearchCount(recipe, _settingsService.Current.AutoTorrent);
        var paginationEnabled = RecipeRuntimeSettings.GetEnableSearchPagination(recipe);
        var paginationPageSize = RecipeRuntimeSettings.GetPaginationPageSize(recipe);
        var paginationMaxPages = GetPaginationMaxPages(recipe);
        var paginationMaxTotalResults = RecipeRuntimeSettings.GetPaginationMaxTotalResults(recipe);
        var paginationIdleTimeoutSeconds = GetPaginationIdleTimeoutSeconds(recipe);
        var searchIdleTimeoutSeconds = GetSearchIdleTimeoutSeconds(recipe);
        var plannedQueries = queries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalQueries = plannedQueries.Count;
        _logger.Info(
            $"Recipe search starting {totalQueries} query(ies). Recipe='{recipe.Name}': {string.Join(" | ", plannedQueries)}",
            LogTarget.All);
        if (RecipeRuntimeSettings.GetEnableCandidateDebugLog(recipe))
        {
            var debugLogFolder = Path.Combine(_settingsService.Current.StateFolder, AppConstants.LogFolderName);
            _logger.Info(
                $"Cart debug mode is enabled for recipe '{recipe.Name}'. Candidate debug log will be written under '{debugLogFolder}' after this run.",
                LogTarget.All);
        }

        var throttler = new SemaphoreSlim(parallelSearches);
        var completedQueries = 0;
        var tasks = plannedQueries
            .Select(async query =>
            {
                await throttler.WaitAsync(cancellationToken);
                try
                {
                    var queryResults = paginationEnabled
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
                    var completed = Interlocked.Increment(ref completedQueries);
                    _progressService.Report(completed, $"Search: Query {completed}/{totalQueries}");
                    _logger.Info(
                        $"Recipe search query succeeded {completed}/{totalQueries}. Remaining={totalQueries - completed}. Query='{query}'. Results={queryResults.Count}.",
                        LogTarget.All);
                    return queryResults;
                }
                finally
                {
                    throttler.Release();
                }
            })
            .ToList();

        var results = (await Task.WhenAll(tasks))
            .SelectMany(item => item)
            .ToList();

        return results
            .GroupBy(result => result.FileUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(result => result.Seeders).First())
            .OrderByDescending(result => result.Seeders)
            .ToList();
    }

    private int GetPaginationMaxPages(SearchRecipe recipe)
    {
        if (recipe.TargetKind == MediaKind.Movie)
        {
            return RecipeRuntimeSettings.GetPaginationMaxPagesMovie(recipe);
        }

        if (recipe.TargetKind == MediaKind.TvSeasonPack)
        {
            return RecipeRuntimeSettings.GetPaginationMaxPagesTvSnapshot(recipe);
        }

        return RecipeRuntimeSettings.GetUseShowSnapshotSearch(recipe, _settingsService.Current.AutoTorrent)
            ? RecipeRuntimeSettings.GetPaginationMaxPagesTvSnapshot(recipe)
            : RecipeRuntimeSettings.GetPaginationMaxPagesTvParallel(recipe);
    }

    private int GetPaginationIdleTimeoutSeconds(SearchRecipe recipe)
    {
        if (recipe.TargetKind == MediaKind.Movie)
        {
            return RecipeRuntimeSettings.GetPaginationIdleTimeoutSecondsMovie(recipe);
        }

        if (recipe.TargetKind == MediaKind.TvSeasonPack)
        {
            return RecipeRuntimeSettings.GetPaginationIdleTimeoutSecondsTvSnapshot(recipe);
        }

        return RecipeRuntimeSettings.GetUseShowSnapshotSearch(recipe, _settingsService.Current.AutoTorrent)
            ? RecipeRuntimeSettings.GetPaginationIdleTimeoutSecondsTvSnapshot(recipe)
            : RecipeRuntimeSettings.GetPaginationIdleTimeoutSecondsTvParallel(recipe);
    }

    private int GetSearchIdleTimeoutSeconds(SearchRecipe recipe)
    {
        return recipe.TargetKind == MediaKind.Movie
            ? RecipeRuntimeSettings.GetSearchIdleTimeoutSecondsMovie(recipe)
            : RecipeRuntimeSettings.GetSearchIdleTimeoutSecondsTvParallel(recipe);
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
        // qBittorrent's search/results offset is a slice into the search job's own accumulating
        // result buffer, not a request for the engine to crawl another page. Restarting the offset
        // at 0 every poll cycle only re-reads what was already seen and never reaches results deeper
        // in the buffer, so we keep advancing a cursor forward across the whole search lifetime.
        var nextOffset = 0;
        var latestTotal = 0;
        var pollCycle = 0;
        var searchStartedAt = DateTimeOffset.UtcNow;

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
                pollCycle++;
                var cycleStartedMerged = mergedByUrl.Count;
                var cycleStartedOffset = nextOffset;
                var cycleRawRows = 0;
                var cyclePages = 0;

                // Always poll at least once per outer cycle even when nextOffset == latestTotal.
                // qBittorrent appends new engine results to the end of the buffer; if we skip the
                // API call after catching up, we never observe total growth and idle-timeout fires
                // while slower engines are still writing into an unread tail.
                for (var pagesThisCycle = 0;
                     pagesThisCycle < effectiveMaxPages && mergedByUrl.Count < effectiveMaxTotal;
                     pagesThisCycle++)
                {
                    var response = await _qbittorrentClient.GetSearchResultsAsync(
                        searchId.Value,
                        effectivePageSize,
                        nextOffset,
                        cancellationToken);
                    latestStatus = response.Status;
                    latestTotal = Math.Max(latestTotal, response.Total);
                    pagesFetched++;
                    cyclePages++;
                    rawRows += response.Results.Count;
                    cycleRawRows += response.Results.Count;
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

                    if (response.Results.Count == 0)
                    {
                        break;
                    }

                    nextOffset += response.Results.Count;

                    if (response.Results.Count < effectivePageSize)
                    {
                        break;
                    }
                }

                var engineSummary = SearchEngineDiagnostics.BuildEngineSummaryIncludingEmpty(requestedEngineNames, mergedByUrl.Values);
                var idleRemainingSeconds = idleTimeout is null
                    ? -1
                    : Math.Max(0, (int)Math.Ceiling((idleDeadline - DateTimeOffset.UtcNow).TotalSeconds));
                _logger.Info(
                    $"Recipe search pagination cycle={pollCycle} query='{query}' elapsedSec={(DateTimeOffset.UtcNow - searchStartedAt).TotalSeconds:0.0} status='{latestStatus}' bufferTotal={latestTotal} cursor={cycleStartedOffset}->{nextOffset} pageRows={cycleRawRows} pages={cyclePages} merged={cycleStartedMerged}->{mergedByUrl.Count} idleRemainingSec={idleRemainingSeconds} engines=[{engineSummary}].",
                    LogTarget.File);

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

                if (mergedByUrl.Count >= effectiveMaxTotal)
                {
                    endedBy = "max-results";
                    break;
                }

                if (!string.Equals(latestStatus, "Running", StringComparison.OrdinalIgnoreCase) && nextOffset >= latestTotal)
                {
                    // Engine(s) finished and we've drained every result they reported; nothing left to page in.
                    endedBy = "status-finished";
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
            $"Recipe search pagination query='{query}' pages={pagesFetched} rawRows={rawRows} mergedRows={merged.Count} bufferTotal={latestTotal} cursorOffset={nextOffset} status='{latestStatus}' pageSize={effectivePageSize} maxPages={effectiveMaxPages} cap={effectiveMaxTotal} idleSeconds={idleTimeoutSeconds} endedBy='{endedBy}' engines=[{SearchEngineDiagnostics.BuildEngineSummaryIncludingEmpty(requestedEngineNames, merged)}] pollCycles={pollCycle}.",
            LogTarget.All);
        SearchEngineDiagnostics.LogEmptyEngines(_logger, requestedEngineNames, merged, query);
        return merged;
    }

    private static string BuildEngineSummary(IEnumerable<TorrentSearchResult> results)
    {
        return SearchEngineDiagnostics.BuildEngineSummary(results);
    }

    private static RecipeDryRunResult BuildDryRunResult(
        SearchRecipe recipe,
        string title,
        IReadOnlyList<string> queries,
        IReadOnlyList<RecipeCandidateResult> evaluated)
    {
        return new RecipeDryRunResult
        {
            Recipe = recipe,
            TargetTitle = title,
            Queries = queries.ToList(),
            AcceptedCandidates = evaluated
                .Where(candidate => candidate.IsAccepted)
                .OrderByDescending(candidate => candidate.TotalScore)
                .ThenByDescending(candidate => candidate.AudioScore)
                .ThenByDescending(candidate => candidate.PreferTermsScore)
                .ThenByDescending(candidate => candidate.SizeScore)
                .ToList(),
            RejectedCandidates = evaluated
                .Where(candidate => !candidate.IsAccepted)
                .ToList()
        };
    }

    private (TrackedShow Show, TrackedEpisode Episode) GetEpisode(RecipeRunRequest request)
    {
        if (request.ShowId is null)
        {
            throw new InvalidOperationException("Select a show for recipe run.");
        }

        var show = _databaseService.GetTrackedShow(request.ShowId.Value)
            ?? throw new InvalidOperationException("Tracked show was not found.");
        var episodes = _databaseService.GetTrackedEpisodes(show.Id)
            .Where(episode => episode.Availability == EpisodeAvailability.Missing)
            .OrderBy(episode => episode.SeasonNumber)
            .ThenBy(episode => episode.EpisodeNumber)
            .ToList();
        var episode = request.SeasonNumber is not null && request.EpisodeNumber is not null
            ? episodes.FirstOrDefault(item => item.SeasonNumber == request.SeasonNumber && item.EpisodeNumber == request.EpisodeNumber)
            : episodes.FirstOrDefault();
        return episode is null
            ? throw new InvalidOperationException("No missing episode is available for recipe run.")
            : (show, episode);
    }

    private TrackedMovie GetMovie(RecipeRunRequest request)
    {
        if (request.MovieId is null)
        {
            throw new InvalidOperationException("Select a movie for recipe run.");
        }

        return _databaseService.GetTrackedMovie(request.MovieId.Value)
            ?? throw new InvalidOperationException("Tracked movie was not found.");
    }

    private AddTorrentRequest CreateAddTorrentRequest(MediaKind targetKind, TorrentSearchResult result, string? itemSavePath)
    {
        var savePath = FirstNonEmpty(itemSavePath, _settingsService.Current.AutoTorrent.DownloadFolder, _settingsService.Current.SourceFolders.FirstOrDefault());
        return new AddTorrentRequest
        {
            Url = result.FileUrl,
            PluginName = result.EngineName,
            SavePath = savePath,
            Category = _settingsService.Current.AutoTorrent.GetCategoryFor(targetKind),
            Tags = "media-manager",
            Paused = false
        };
    }

    private static EpisodeFetchCandidate ToEpisodeCandidate(long episodeId, RecipeCandidateResult candidate)
    {
        return new EpisodeFetchCandidate
        {
            EpisodeId = episodeId,
            FileName = candidate.SearchResult.FileName,
            FileUrl = candidate.SearchResult.FileUrl,
            FileSize = candidate.SearchResult.FileSize,
            Seeders = candidate.SearchResult.Seeders,
            Leechers = candidate.SearchResult.Leechers,
            PluginName = candidate.SearchResult.EngineName,
            QualityLabel = TorrentQuality.Detect(candidate.SearchResult.FileName),
            AudioCodecLabel = string.Empty,
            QualityScore = candidate.QualityScore,
            TotalScore = candidate.TotalScore
        };
    }

    private static EpisodeFetchCandidate ToMovieCandidate(long movieId, RecipeCandidateResult candidate)
    {
        var movieCandidate = ToEpisodeCandidate(0, candidate);
        movieCandidate.MovieId = movieId;
        return movieCandidate;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private void TryWriteCandidateDebugLog(
        SearchRecipe recipe,
        string targetTitle,
        MediaKind targetKind,
        IReadOnlyList<string> queries,
        IReadOnlyList<TorrentSearchResult> searchResults,
        IReadOnlyList<RecipeCandidateResult> evaluated)
    {
        if (!RecipeRuntimeSettings.GetEnableCandidateDebugLog(recipe))
        {
            return;
        }

        var logSession = new CartCandidateDebugSession(
            _settingsService.Current.StateFolder,
            _settingsService.Current.Logs.MaxLinesPerFile);
        logSession.WriteLine(
            $"Media='{SanitizeForLog(targetTitle)}' Recipe='{SanitizeForLog(recipe.Name)}' RecipeId='{recipe.RecipeId}' Target='{targetKind}'");
        logSession.WriteLine($"Queries={queries.Count} => {string.Join(" | ", queries.Select(SanitizeForLog))}");
        var timeoutSeconds = targetKind == MediaKind.Movie
            ? RecipeRuntimeSettings.GetMovieSearchTimeoutSeconds(recipe, _settingsService.Current.AutoTorrent)
            : RecipeRuntimeSettings.GetParallelSearchTimeoutSeconds(recipe, _settingsService.Current.AutoTorrent);
        logSession.WriteLine(
            $"SearchResults={searchResults.Count} TimeoutSeconds={timeoutSeconds} Accepted={evaluated.Count(item => item.IsAccepted)} Rejected={evaluated.Count(item => !item.IsAccepted)}");
        foreach (var summary in evaluated
                     .Where(item => !item.IsAccepted)
                     .GroupBy(item => item.RejectReason)
                     .OrderByDescending(group => group.Count()))
        {
            logSession.WriteLine($"RejectSummary reason={summary.Key} count={summary.Count()}");
        }

        foreach (var item in evaluated)
        {
            var result = item.SearchResult;
            var sizeGb = result.FileSize > 0
                ? (result.FileSize / (1024d * 1024d * 1024d)).ToString("0.00")
                : "unknown";
            var verdict = item.IsAccepted ? "ACCEPT" : "REJECT";
            var detail = item.IsAccepted ? "-" : SanitizeForLog(item.RejectDetail);
            logSession.WriteLine(
                $"{verdict} reason={item.RejectReason} detail='{detail}' quality='{TorrentQuality.Detect(result.FileName)}' sizeGB={sizeGb} seeders={result.Seeders} " +
                $"Q={item.QualityScore} A={item.AudioScore} Pref={item.PreferTermsScore} Size={item.SizeScore} Total={item.TotalScore} " +
                $"engine='{SanitizeForLog(result.EngineName)}' linkType='{result.LinkType}' name='{SanitizeForLog(result.FileName)}'");
        }

        _logger.Info($"Cart debug log: {logSession.FirstFilePath}", LogTarget.All);
    }

    private static string SanitizeForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Replace("'", "''")
            .Trim();
    }
}
