using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class AutomationFlowService : IAutomationFlowService
{
    private readonly IDatabaseService _databaseService;
    private readonly IRecipeService _recipeService;
    private readonly ISearchPlanBuilder _searchPlanBuilder;
    private readonly ICandidateEvaluationService _candidateEvaluationService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;

    public AutomationFlowService(
        IDatabaseService databaseService,
        IRecipeService recipeService,
        ISearchPlanBuilder searchPlanBuilder,
        ICandidateEvaluationService candidateEvaluationService,
        IQbittorrentClient qbittorrentClient,
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        ISettingsService settingsService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _recipeService = recipeService;
        _searchPlanBuilder = searchPlanBuilder;
        _candidateEvaluationService = candidateEvaluationService;
        _qbittorrentClient = qbittorrentClient;
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _settingsService = settingsService;
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
            var added = await _qbittorrentClient.AddTorrentAsync(CreateAddTorrentRequest(result.Recipe, candidate.SearchResult, null), cancellationToken);
            _trackedMovieService.UpdateSelectedCandidate(movie.Id, ToMovieCandidate(movie.Id, candidate));
            _trackedMovieService.UpdateTorrentState(movie.Id, added);
            _logger.Info($"Recipe run added movie torrent for {movie.DisplayTitle}: {added.Name}", LogTarget.All);
            return result;
        }

        var (show, episode) = GetEpisode(request);
        var season = _databaseService.GetTrackedSeasons(show.Id)
            .FirstOrDefault(item => item.SeasonNumber == episode.SeasonNumber);
        var addedTorrent = await _qbittorrentClient.AddTorrentAsync(CreateAddTorrentRequest(result.Recipe, candidate.SearchResult, season?.DownloadFolder), cancellationToken);
        var selectedCandidate = ToEpisodeCandidate(episode.Id, candidate);
        _trackedShowService.UpdateSelectedCandidate(episode.Id, selectedCandidate);
        _trackedShowService.UpdateTorrentState(episode.Id, addedTorrent);
        _logger.Info($"Recipe run added torrent for {show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}: {addedTorrent.Name}", LogTarget.All);
        return result;
    }

    private async Task<RecipeDryRunResult> DryRunEpisodeAsync(RecipeRunRequest request, CancellationToken cancellationToken)
    {
        var (show, episode) = GetEpisode(request);
        var recipe = _recipeService.GetRecipeOrDefault(request.RecipeId ?? show.RecipeId, MediaKind.TvEpisode);
        var queries = _searchPlanBuilder.BuildEpisodeQueries(recipe, show, episode);
        var results = await SearchAsync(recipe, queries, cancellationToken);
        var evaluated = results
            .Select(result => _candidateEvaluationService.EvaluateEpisode(recipe, show, episode, result))
            .ToList();
        return BuildDryRunResult(recipe, show.DisplayTitle, queries, evaluated);
    }

    private async Task<RecipeDryRunResult> DryRunMovieAsync(RecipeRunRequest request, CancellationToken cancellationToken)
    {
        var movie = GetMovie(request);
        var recipe = _recipeService.GetRecipeOrDefault(request.RecipeId ?? movie.RecipeId, MediaKind.Movie);
        var queries = _searchPlanBuilder.BuildMovieQueries(recipe, movie);
        var results = await SearchAsync(recipe, queries, cancellationToken);
        var evaluated = results
            .Select(result => _candidateEvaluationService.EvaluateMovie(recipe, movie, result))
            .ToList();
        return BuildDryRunResult(recipe, movie.DisplayTitle, queries, evaluated);
    }

    private async Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(
        SearchRecipe recipe,
        IReadOnlyList<string> queries,
        CancellationToken cancellationToken)
    {
        var searchSource = recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.SearchSource && module.IsEnabled);
        var requestLimit = searchSource?.ResultLimit is > 0 ? searchSource.ResultLimit : 100;
        var plugins = string.IsNullOrWhiteSpace(searchSource?.Plugins) ? "enabled" : searchSource!.Plugins;
        var category = string.IsNullOrWhiteSpace(searchSource?.Category) ? "all" : searchSource!.Category;
        var parallelSearches = RecipeRuntimeSettings.GetParallelSearchCount(recipe, _settingsService.Current.AutoTorrent);
        var throttler = new SemaphoreSlim(parallelSearches);
        var tasks = queries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(async query =>
            {
                await throttler.WaitAsync(cancellationToken);
                try
                {
                    return await _qbittorrentClient.SearchAsync(new TorrentSearchRequest
                    {
                        Query = query,
                        Plugins = plugins,
                        Category = category,
                        Limit = requestLimit
                    }, cancellationToken);
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
            .OrderByDescending(episode => episode.IsWanted)
            .ThenBy(episode => episode.SeasonNumber)
            .ThenBy(episode => episode.EpisodeNumber)
            .ToList();
        var episode = request.SeasonNumber is not null && request.EpisodeNumber is not null
            ? episodes.FirstOrDefault(item => item.SeasonNumber == request.SeasonNumber && item.EpisodeNumber == request.EpisodeNumber)
            : episodes.FirstOrDefault(item => item.IsWanted) ?? episodes.FirstOrDefault();
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

    private AddTorrentRequest CreateAddTorrentRequest(SearchRecipe recipe, TorrentSearchResult result, string? itemSavePath)
    {
        var savePath = FirstNonEmpty(itemSavePath, _settingsService.Current.AutoTorrent.DownloadFolder, _settingsService.Current.SourceFolders.FirstOrDefault());
        var category = FirstNonEmpty(_settingsService.Current.AutoTorrent.CategoryName, "AutoTorrent");
        return new AddTorrentRequest
        {
            Url = result.FileUrl,
            PluginName = result.EngineName,
            SavePath = savePath,
            Category = category,
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
}
