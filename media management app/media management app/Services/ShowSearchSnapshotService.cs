using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class ShowSearchSnapshotService
{
    private const int SnapshotPollDelayMilliseconds = 1000;

    private readonly ISettingsService _settingsService;
    private readonly IRecipeService _recipeService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IAppLogger _logger;

    public ShowSearchSnapshotService(
        ISettingsService settingsService,
        IRecipeService recipeService,
        IQbittorrentClient qbittorrentClient,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _recipeService = recipeService;
        _qbittorrentClient = qbittorrentClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TorrentSearchResult>> CaptureSnapshotAsync(
        TrackedShow show,
        IOperationProgressService? progressService,
        CancellationToken cancellationToken,
        MediaKind recipeKind = MediaKind.TvEpisode)
    {
        if (progressService is null)
        {
            _logger.Info($"Starting snapshot search for {show.DisplayTitle}.", LogTarget.All);
        }
        var fallback = _settingsService.Current.AutoTorrent;
        var recipe = recipeKind == MediaKind.TvSeasonPack
            ? _recipeService.GetRecipeOrDefault(show.PackRecipeId, MediaKind.TvSeasonPack)
            : _recipeService.GetRecipeOrDefault(show.RecipeId, MediaKind.TvEpisode);
        var targetResults = RecipeRuntimeSettings.GetSnapshotTargetResults(recipe, fallback);
        var timeoutSeconds = RecipeRuntimeSettings.GetSnapshotTimeoutSeconds(recipe, fallback);
        var query = BuildPrimaryQuery(show);
        var snapshot = await CaptureSnapshotAsync(query, targetResults, timeoutSeconds, progressService, cancellationToken);
        if (snapshot.Count >= Math.Max(targetResults / 2, 50) || show.FirstAirYear is null)
        {
            return snapshot;
        }

        var fallbackQuery = show.Title;
        if (string.Equals(fallbackQuery, query, StringComparison.OrdinalIgnoreCase))
        {
            return snapshot;
        }

        var fallbackSnapshot = await CaptureSnapshotAsync(fallbackQuery, targetResults, timeoutSeconds, progressService, cancellationToken);
        var combined = snapshot
            .Concat(fallbackSnapshot)
            .GroupBy(result => result.FileUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(result => result.Seeders).First())
            .ToList();
        return combined;
    }

    private static string BuildPrimaryQuery(TrackedShow show)
    {
        return show.FirstAirYear is null
            ? show.Title
            : $"{show.Title} {show.FirstAirYear}";
    }

    private async Task<IReadOnlyList<TorrentSearchResult>> CaptureSnapshotAsync(
        string query,
        int targetResults,
        int timeoutSeconds,
        IOperationProgressService? progressService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        targetResults = Math.Clamp(targetResults, 100, 5000);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 30, 300));
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        int? searchId = null;
        IReadOnlyList<TorrentSearchResult> latestResults = [];
        var latestStatus = "Running";

        try
        {
            progressService?.Start($"Searching snapshot: {query}", targetResults);
            searchId = await _qbittorrentClient.StartSearchAsync(new TorrentSearchRequest { Query = query }, cancellationToken);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await _qbittorrentClient.GetSearchResultsAsync(searchId.Value, limit: targetResults, cancellationToken);
                latestResults = response.Results;
                latestStatus = response.Status;
                progressService?.Report(latestResults.Count, $"Searching snapshot: {query}");

                if (!string.Equals(latestStatus, "Running", StringComparison.OrdinalIgnoreCase) ||
                    latestResults.Count >= targetResults)
                {
                    break;
                }

                await Task.Delay(SnapshotPollDelayMilliseconds, cancellationToken);
            }

            _logger.Info(
                $"Snapshot search completed. Query='{query}', Status='{latestStatus}', Results={latestResults.Count}.",
                LogTarget.All);
            progressService?.Finish($"Snapshot search finished: {query} ({latestResults.Count})");

            return latestResults
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

            if (cancellationToken.IsCancellationRequested)
            {
                progressService?.Finish($"Snapshot search canceled: {query}");
            }
        }
    }
}
