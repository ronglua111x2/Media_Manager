using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class ShowSearchSnapshotService
{
    private const int SnapshotPollDelayMilliseconds = 1000;

    private readonly ISettingsService _settingsService;
    private readonly IRecipeService _recipeService;
    private readonly ISearchPlanBuilder _searchPlanBuilder;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IQbittorrentSearchPluginService _searchPluginService;
    private readonly IAppLogger _logger;

    public ShowSearchSnapshotService(
        ISettingsService settingsService,
        IRecipeService recipeService,
        ISearchPlanBuilder searchPlanBuilder,
        IQbittorrentClient qbittorrentClient,
        IQbittorrentSearchPluginService searchPluginService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _recipeService = recipeService;
        _searchPlanBuilder = searchPlanBuilder;
        _qbittorrentClient = qbittorrentClient;
        _searchPluginService = searchPluginService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TorrentSearchResult>> CaptureSnapshotAsync(
        TrackedShow show,
        IOperationProgressService? progressService,
        CancellationToken cancellationToken,
        MediaKind recipeKind = MediaKind.TvEpisode,
        string? recipeId = null)
    {
        if (progressService is null)
        {
            _logger.Info($"Starting snapshot search for {show.DisplayTitle}.", LogTarget.All);
        }

        var fallback = _settingsService.Current.AutoTorrent;
        var recipe = recipeKind == MediaKind.TvSeasonPack
            ? _recipeService.GetRecipeOrDefault(recipeId ?? show.PackRecipeId, MediaKind.TvSeasonPack)
            : _recipeService.GetRecipeOrDefault(recipeId ?? show.RecipeId, MediaKind.TvEpisode);
        var targetResults = RecipeRuntimeSettings.GetSnapshotTargetResults(recipe, fallback);
        var timeoutSeconds = RecipeRuntimeSettings.GetSnapshotTimeoutSeconds(recipe, fallback);
        var idleTimeoutSeconds = RecipeRuntimeSettings.GetSnapshotIdleTimeoutSeconds(recipe, fallback);
        var queries = _searchPlanBuilder.BuildShowSnapshotQueries(recipe, show);
        if (queries.Count == 0)
        {
            _logger.Info($"Snapshot search skipped for {show.DisplayTitle}: no queries were built from recipe '{recipe.Name}'.", LogTarget.All);
            return [];
        }

        var totalQueries = queries.Count;
        _logger.Info(
            $"Snapshot search starting {totalQueries} query(ies) for {show.DisplayTitle}. Recipe='{recipe.Name}': {string.Join(" | ", queries)}",
            LogTarget.All);

        progressService?.Start($"Search: Query 0/{totalQueries}", 0);

        var searchSource = recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.SearchSource && module.IsEnabled);
        var savedPlugins = string.IsNullOrWhiteSpace(searchSource?.Plugins) ? "enabled" : searchSource!.Plugins;
        var livePlugins = await _searchPluginService.GetPluginsAsync(cancellationToken);
        var resolved = _searchPluginService.ResolveForSearch(savedPlugins, livePlugins, _logger);
        if (resolved.SkipSearch)
        {
            _logger.Warning($"Snapshot search skipped for {show.DisplayTitle}: no valid enabled plugins remain.", LogTarget.All);
            return [];
        }

        var combined = new List<TorrentSearchResult>();
        var completedQueries = 0;
        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await CaptureSnapshotAsync(
                query,
                targetResults,
                timeoutSeconds,
                idleTimeoutSeconds,
                resolved.PluginsForApi,
                resolved.RequestedNames,
                cancellationToken);
            completedQueries++;
            progressService?.Report(completedQueries, $"Search: Query {completedQueries}/{totalQueries}");
            _logger.Info(
                $"Snapshot search query succeeded {completedQueries}/{totalQueries}. Remaining={totalQueries - completedQueries}. Query='{query}'. Results={snapshot.Count}.",
                LogTarget.All);

            combined = combined
                .Concat(snapshot)
                .GroupBy(result => result.FileUrl, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(result => result.Seeders).First())
                .ToList();

            if (combined.Count >= targetResults)
            {
                if (completedQueries < totalQueries)
                {
                    _logger.Info(
                        $"Snapshot search stopped early for {show.DisplayTitle}: reached target results ({targetResults}). Completed={completedQueries}/{totalQueries}, Skipped={totalQueries - completedQueries}.",
                        LogTarget.All);
                }

                break;
            }
        }

        return combined
            .OrderByDescending(result => result.Seeders)
            .ThenBy(result => result.FileSize)
            .ToList();
    }

    private async Task<IReadOnlyList<TorrentSearchResult>> CaptureSnapshotAsync(
        string query,
        int targetResults,
        int timeoutSeconds,
        int idleTimeoutSeconds,
        string plugins,
        IReadOnlyList<string> requestedEngineNames,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        targetResults = Math.Clamp(targetResults, 100, 5000);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 30, 300));
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var idleTimeout = idleTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(Math.Clamp(idleTimeoutSeconds, 3, 120))
            : (TimeSpan?)null;
        int? searchId = null;
        IReadOnlyList<TorrentSearchResult> latestResults = [];
        var latestStatus = "Running";
        var mergedByUrl = new Dictionary<string, TorrentSearchResult>(StringComparer.OrdinalIgnoreCase);
        var lastResultCount = 0;
        var idleDeadline = idleTimeout is null ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.Add(idleTimeout.Value);
        var endedBy = "timeout";

        try
        {
            searchId = await _qbittorrentClient.StartSearchAsync(new TorrentSearchRequest
            {
                Query = query,
                Plugins = plugins
            }, cancellationToken);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await _qbittorrentClient.GetSearchResultsAsync(searchId.Value, limit: targetResults, offset: 0, cancellationToken);
                latestResults = response.Results;
                latestStatus = response.Status;
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

                if (mergedByUrl.Count > lastResultCount)
                {
                    lastResultCount = mergedByUrl.Count;
                    if (idleTimeout is not null)
                    {
                        idleDeadline = DateTimeOffset.UtcNow.Add(idleTimeout.Value);
                    }
                }
                else if (idleTimeout is not null && DateTimeOffset.UtcNow >= idleDeadline)
                {
                    endedBy = "idle-timeout";
                    _logger.Info(
                        $"Snapshot search idle timeout reached. Query='{query}', Results={mergedByUrl.Count}, IdleSeconds={idleTimeout.Value.TotalSeconds:0}.",
                        LogTarget.All);
                    break;
                }

                if (!string.Equals(latestStatus, "Running", StringComparison.OrdinalIgnoreCase) ||
                    mergedByUrl.Count >= targetResults)
                {
                    endedBy = !string.Equals(latestStatus, "Running", StringComparison.OrdinalIgnoreCase)
                        ? "status-finished"
                        : "max-results";
                    break;
                }

                await Task.Delay(SnapshotPollDelayMilliseconds, cancellationToken);
            }

            _logger.Info(
                $"Snapshot search completed. Query='{query}', Status='{latestStatus}', Results={mergedByUrl.Count}, EndedBy='{HuntLogFormatter.FormatEndedBy(endedBy, mergedByUrl.Count)}', engines=[{SearchEngineDiagnostics.BuildEngineSummaryIncludingEmpty(requestedEngineNames, mergedByUrl.Values)}].",
                LogTarget.All);
            SearchEngineDiagnostics.LogEmptyEngines(_logger, requestedEngineNames, mergedByUrl.Values, query);

            return mergedByUrl.Values
                .OrderByDescending(result => result.Seeders)
                .ThenBy(result => result.FileSize)
                .ToList();
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
    }
}
