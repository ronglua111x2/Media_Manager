using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class FindAddViewModel : ViewModelBase
{
    private const int MaxConcurrentDetailLoads = 4;

    private static readonly HttpClient PosterHttpClient = new();

    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly ITmdbShowCatalogService _showCatalogService;
    private readonly ITmdbMovieCatalogService _movieCatalogService;
    private readonly IRecipeService _recipeService;
    private readonly IAppLogger _logger;

    private IReadOnlyList<SearchRecipe> _allRecipes = [];
    private IReadOnlyList<FindAddMediaCardViewModel> _allMediaCards = [];
    private readonly Dictionary<string, ImageSource> _posterImageCache = [];
    private CancellationTokenSource? _searchCancellation;

    public FindAddViewModel(
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        ITmdbShowCatalogService showCatalogService,
        ITmdbMovieCatalogService movieCatalogService,
        IRecipeService recipeService,
        IAppLogger logger)
    {
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _showCatalogService = showCatalogService;
        _movieCatalogService = movieCatalogService;
        _recipeService = recipeService;
        _logger = logger;

        SearchResultsView = CollectionViewSource.GetDefaultView(SearchResults);
        ApplySearchResultSort();
        _recipeService.RecipesChanged += OnRecipesChanged;
        LoadRecipes();
        RefreshExistingMedia();
        StatusMessage = "Search TMDB to add movies or shows to your library.";
    }

    public ObservableCollection<TmdbUnifiedSearchResult> SearchResults { get; } = [];

    public ICollectionView SearchResultsView { get; }

    public ObservableCollection<FindAddMediaCardViewModel> ExistingMediaCards { get; } = [];

    public ObservableCollection<SearchRecipe> RecipeOptions { get; } = [];

    public IReadOnlyList<MediaCardSortMode> SortModes { get; } =
    [
        MediaCardSortMode.DateAddedDesc,
        MediaCardSortMode.TypeThenTitle,
        MediaCardSortMode.Title
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string searchText = string.Empty;

    [ObservableProperty]
    private TmdbUnifiedSearchResult? selectedResult;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedToLibraryCommand))]
    private string? selectedRecipeId;

    [ObservableProperty]
    private MediaCardSortMode mediaSortMode = MediaCardSortMode.DateAddedDesc;

    [ObservableProperty]
    private bool isResultDetailsCompact;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSearchCommand))]
    private bool isSearching;

    [ObservableProperty]
    private bool isLoadingDetails;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedToLibraryCommand))]
    private bool isAddingToLibrary;

    [ObservableProperty]
    private int discoveredResultCount;

    [ObservableProperty]
    private int loadedResultCount;

    [ObservableProperty]
    private SearchResultSortColumn selectedSearchResultSortColumn = SearchResultSortColumn.Name;

    [ObservableProperty]
    private ListSortDirection searchResultSortDirection = ListSortDirection.Ascending;

    public bool HasSearchResults => SearchResults.Count > 0;

    public bool HasExistingMedia => ExistingMediaCards.Count > 0;

    public bool HasSelectedResult => SelectedResult is not null;

    public string SelectedResultHeading => SelectedResult is null
        ? "Select a result"
        : SelectedResult.Title;

    public string SelectedResultSubheading => SelectedResult is null
        ? "Search results will show details here."
        : $"{SelectedResult.TypeLabel} - {SelectedResult.YearLabel} - {SelectedResult.StatLabel}";

    public string AddButtonText => SelectedResult?.IsAlreadyAdded == true ? "Already Added" : "Add to Library";

    public bool IsDateSortSelected => MediaSortMode == MediaCardSortMode.DateAddedDesc;

    public bool IsTypeSortSelected => MediaSortMode == MediaCardSortMode.TypeThenTitle;

    public bool IsNameSortSelected => MediaSortMode == MediaCardSortMode.Title;

    public string NameSortIndicator => GetSortIndicator(SearchResultSortColumn.Name);

    public string TypeSortIndicator => GetSortIndicator(SearchResultSortColumn.Type);

    public string YearSortIndicator => GetSortIndicator(SearchResultSortColumn.Year);

    public string StatsSortIndicator => GetSortIndicator(SearchResultSortColumn.Stats);

    public string StatusSortIndicator => GetSortIndicator(SearchResultSortColumn.Status);

    [RelayCommand(CanExecute = nameof(CanSearch), AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        var query = SearchText.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResults.Clear();
            SelectedResult = null;
            StatusMessage = "Enter a title to search TMDB.";
            OnPropertyChanged(nameof(HasSearchResults));
            return;
        }

        _searchCancellation?.Cancel();
        var searchCancellation = new CancellationTokenSource();
        _searchCancellation = searchCancellation;
        var cancellationToken = searchCancellation.Token;

        IsSearching = true;
        StatusMessage = $"Searching TMDB for '{query}'...";
        SearchResults.Clear();
        _posterImageCache.Clear();
        SelectedResult = null;
        DiscoveredResultCount = 0;
        LoadedResultCount = 0;
        OnPropertyChanged(nameof(HasSearchResults));

        try
        {
            var showTask = SearchShowsLightweightSafelyAsync(query, cancellationToken);
            var movieTask = SearchMoviesSafelyAsync(query, cancellationToken);
            await Task.WhenAll(showTask, movieTask);

            if (!string.IsNullOrWhiteSpace(showTask.Result.ErrorMessage) &&
                !string.IsNullOrWhiteSpace(movieTask.Result.ErrorMessage))
            {
                throw new InvalidOperationException(BuildSearchFailureMessage(showTask.Result.ErrorMessage, movieTask.Result.ErrorMessage));
            }

            var existingShows = _trackedShowService.GetShows().Select(show => show.TmdbId).ToHashSet();
            var existingMovies = _trackedMovieService.GetMovies().Select(movie => movie.TmdbId).ToHashSet();
            var candidates = showTask.Result.Results.Select(show => new TmdbUnifiedSearchResult
                {
                    TmdbId = show.TmdbId,
                    Title = show.Title,
                    MediaKind = MediaKind.TvEpisode,
                    Year = show.FirstAirYear,
                    Overview = show.Overview,
                    PosterPath = show.PosterPath,
                    SeasonCount = show.SeasonCount,
                    EpisodeCount = show.EpisodeCount,
                    IsAlreadyAdded = existingShows.Contains(show.TmdbId)
                })
                .Concat(movieTask.Result.Results.Select(movie => new TmdbUnifiedSearchResult
                {
                    TmdbId = movie.TmdbId,
                    Title = movie.Title,
                    MediaKind = MediaKind.Movie,
                    Year = movie.ReleaseYear,
                    Overview = movie.Overview,
                    PosterPath = movie.PosterPath,
                    IsAlreadyAdded = existingMovies.Contains(movie.TmdbId)
                }))
                .OrderBy(result => result.Title)
                .ThenBy(result => result.MediaKind)
                .ThenBy(result => result.Year ?? int.MaxValue)
                .ToList();

            DiscoveredResultCount = candidates.Count;
            if (candidates.Count == 0)
            {
                var emptyWarningSuffix = BuildSearchWarningSuffix(showTask.Result.ErrorMessage, movieTask.Result.ErrorMessage);
                StatusMessage = $"No TMDB results found for '{query}'.{emptyWarningSuffix}";
                return;
            }

            StatusMessage = $"Found {candidates.Count} TMDB result(s). Loading details...";
            await StreamEnrichedResultsAsync(candidates, cancellationToken);

            var warningSuffix = BuildSearchWarningSuffix(showTask.Result.ErrorMessage, movieTask.Result.ErrorMessage);
            if (ReferenceEquals(_searchCancellation, searchCancellation))
            {
                StatusMessage = $"Loaded {LoadedResultCount}/{DiscoveredResultCount} TMDB result(s).{warningSuffix}";
            }
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_searchCancellation, searchCancellation))
            {
                StatusMessage = LoadedResultCount == 0
                    ? "TMDB search canceled."
                    : $"TMDB search canceled after loading {LoadedResultCount}/{DiscoveredResultCount} result(s).";
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_searchCancellation, searchCancellation))
            {
                StatusMessage = ex.Message;
                _logger.Error("Find/Add search failed.", ex, LogTarget.All);
            }
        }
        finally
        {
            searchCancellation.Dispose();
            if (ReferenceEquals(_searchCancellation, searchCancellation))
            {
                IsSearching = false;
                _searchCancellation = null;
            }
            OnPropertyChanged(nameof(HasSearchResults));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddSelectedToLibrary))]
    private async Task AddSelectedToLibraryAsync()
    {
        if (SelectedResult is null || SelectedResult.IsAlreadyAdded)
        {
            return;
        }

        IsAddingToLibrary = true;
        StatusMessage = $"Adding '{SelectedResult.Title}' to library...";
        try
        {
            if (SelectedResult.MediaKind == MediaKind.Movie)
            {
                var movie = await _trackedMovieService.AddMovieAsync(SelectedResult.ToMovieSearchResult());
                _trackedMovieService.UpdateRecipe(movie.Id, SelectedRecipeId);
            }
            else
            {
                var show = await _trackedShowService.AddShowAsync(SelectedResult.ToShowSearchResult());
                _trackedShowService.UpdateRecipe(show.Id, SelectedRecipeId);
                _trackedShowService.UpdatePackRecipe(show.Id, GetDefaultRecipeId(MediaKind.TvSeasonPack));
            }

            SelectedResult.IsAlreadyAdded = true;
            RefreshExistingMedia();
            StatusMessage = $"Added '{SelectedResult.Title}' to library.";
            AddSelectedToLibraryCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(AddButtonText));
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.Error($"Failed to add '{SelectedResult.Title}' to library.", ex, LogTarget.All);
        }
        finally
        {
            IsAddingToLibrary = false;
        }
    }

    [RelayCommand]
    private void RefreshExistingMedia()
    {
        var shows = _trackedShowService.GetShows().Select(show => new FindAddMediaCardViewModel
        {
            Id = show.Id,
            TmdbId = show.TmdbId,
            Title = show.Title,
            MediaKind = MediaKind.TvEpisode,
            Year = show.FirstAirYear,
            CreatedUtc = show.CreatedUtc,
            AvailableCount = show.AvailableEpisodes,
            TotalCount = show.TotalEpisodes
        });
        var movies = _trackedMovieService.GetMovies().Select(movie => new FindAddMediaCardViewModel
        {
            Id = movie.Id,
            TmdbId = movie.TmdbId,
            Title = movie.Title,
            MediaKind = MediaKind.Movie,
            Year = movie.ReleaseYear,
            CreatedUtc = movie.CreatedUtc,
            AvailableCount = movie.Availability == EpisodeAvailability.Available ? 1 : 0,
            TotalCount = 1
        });

        _allMediaCards = shows.Concat(movies).ToList();
        ApplyMediaCardSort();
    }

    [RelayCommand]
    private void SetMediaSortMode(MediaCardSortMode mode)
    {
        MediaSortMode = mode;
    }

    [RelayCommand]
    private void ToggleResultDetailsCompact()
    {
        IsResultDetailsCompact = !IsResultDetailsCompact;
    }

    [RelayCommand(CanExecute = nameof(CanCancelSearch))]
    private void CancelSearch()
    {
        _searchCancellation?.Cancel();
        StatusMessage = LoadedResultCount == 0
            ? "Canceling TMDB search..."
            : $"Canceling TMDB search... loaded {LoadedResultCount}/{DiscoveredResultCount}.";
    }

    [RelayCommand]
    private void SetSearchResultSort(SearchResultSortColumn column)
    {
        if (SelectedSearchResultSortColumn == column)
        {
            SearchResultSortDirection = SearchResultSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            SelectedSearchResultSortColumn = column;
            SearchResultSortDirection = ListSortDirection.Ascending;
        }

        ApplySearchResultSort();
        NotifySearchResultSortIndicatorsChanged();
    }

    partial void OnSelectedResultChanged(TmdbUnifiedSearchResult? value)
    {
        LoadSelectedResultState(value);
        OnPropertyChanged(nameof(HasSelectedResult));
        OnPropertyChanged(nameof(SelectedResultHeading));
        OnPropertyChanged(nameof(SelectedResultSubheading));
        OnPropertyChanged(nameof(AddButtonText));
        AddSelectedToLibraryCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedRecipeIdChanged(string? value)
    {
        if (SelectedResult is not null)
        {
            SelectedResult.SelectedRecipeId = value;
        }
    }

    partial void OnMediaSortModeChanged(MediaCardSortMode value)
    {
        ApplyMediaCardSort();
        OnPropertyChanged(nameof(IsDateSortSelected));
        OnPropertyChanged(nameof(IsTypeSortSelected));
        OnPropertyChanged(nameof(IsNameSortSelected));
    }

    private void OnRecipesChanged(object? sender, EventArgs e)
    {
        LoadRecipes();
        LoadSelectedResultState(SelectedResult);
    }

    private void LoadSelectedResultState(TmdbUnifiedSearchResult? result)
    {
        RecipeOptions.Clear();
        if (result is null)
        {
            SelectedRecipeId = null;
            return;
        }

        foreach (var recipe in GetRecipesFor(result.MediaKind))
        {
            RecipeOptions.Add(recipe);
        }

        SelectedRecipeId = result.SelectedRecipeId ?? GetDefaultRecipeId(result.MediaKind);
        result.SelectedRecipeId = SelectedRecipeId;
    }

    private async Task StreamEnrichedResultsAsync(
        IReadOnlyList<TmdbUnifiedSearchResult> candidates,
        CancellationToken cancellationToken)
    {
        using var detailGate = new SemaphoreSlim(MaxConcurrentDetailLoads);
        var detailTasks = candidates
            .Select(candidate => EnrichSearchResultWithGateAsync(candidate, detailGate, cancellationToken))
            .ToList();

        while (detailTasks.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completedTask = await Task.WhenAny(detailTasks);
            detailTasks.Remove(completedTask);

            var result = await completedTask;
            cancellationToken.ThrowIfCancellationRequested();

            LoadedResultCount++;
            SearchResults.Add(result);
            SearchResultsView.Refresh();
            if (SelectedResult is null)
            {
                SelectedResult = result;
            }

            StatusMessage = $"Loaded {LoadedResultCount}/{DiscoveredResultCount} TMDB result(s)...";
            OnPropertyChanged(nameof(HasSearchResults));
        }
    }

    private async Task<TmdbUnifiedSearchResult> EnrichSearchResultWithGateAsync(
        TmdbUnifiedSearchResult result,
        SemaphoreSlim detailGate,
        CancellationToken cancellationToken)
    {
        await detailGate.WaitAsync(cancellationToken);
        try
        {
            return await EnrichSearchResultAsync(result, cancellationToken);
        }
        finally
        {
            detailGate.Release();
        }
    }

    private async Task<TmdbUnifiedSearchResult> EnrichSearchResultAsync(
        TmdbUnifiedSearchResult result,
        CancellationToken cancellationToken)
    {
        result.SelectedRecipeId = GetDefaultRecipeId(result.MediaKind);
        try
        {
            if (result.MediaKind == MediaKind.Movie)
            {
                var details = await _movieCatalogService.GetMovieDetailsAsync(result.TmdbId, cancellationToken);
                result.Overview = details.Overview;
                result.PosterPath = details.PosterPath;
                result.Year = details.ReleaseYear;
                result.RuntimeMinutes = details.RuntimeMinutes;
            }
            else
            {
                var details = await _showCatalogService.GetTvShowSummaryAsync(result.TmdbId, cancellationToken);
                result.Overview = details.Overview;
                result.PosterPath = details.PosterPath;
                result.Year = details.FirstAirYear;
                result.SeasonCount = details.SeasonCount;
                result.EpisodeCount = details.EpisodeCount;
            }

            result.IsDetailsLoaded = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.IsDetailsLoaded = false;
            _logger.Warning($"TMDB detail load failed for '{result.Title}' ({result.TypeLabel}, id={result.TmdbId}): {ex.Message}", LogTarget.All);
        }

        result.PosterImage = await LoadPosterImageAsync(result.PosterUrl, cancellationToken);
        return result;
    }

    private async Task<ImageSource?> LoadPosterImageAsync(string posterUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(posterUrl))
        {
            return null;
        }

        if (_posterImageCache.TryGetValue(posterUrl, out var cachedImage))
        {
            return cachedImage;
        }

        try
        {
            var bytes = await PosterHttpClient.GetByteArrayAsync(posterUrl, cancellationToken);
            await using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            _posterImageCache[posterUrl] = image;
            return image;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Poster preload failed for '{posterUrl}': {ex.Message}", LogTarget.File | LogTarget.Console);
            return null;
        }
    }

    private void ApplySearchResultSort()
    {
        var primaryProperty = SelectedSearchResultSortColumn switch
        {
            SearchResultSortColumn.Type => nameof(TmdbUnifiedSearchResult.TypeSortValue),
            SearchResultSortColumn.Year => nameof(TmdbUnifiedSearchResult.YearSortValue),
            SearchResultSortColumn.Stats => nameof(TmdbUnifiedSearchResult.StatSortValue),
            SearchResultSortColumn.Status => nameof(TmdbUnifiedSearchResult.StatusSortValue),
            _ => nameof(TmdbUnifiedSearchResult.Title)
        };

        using (SearchResultsView.DeferRefresh())
        {
            SearchResultsView.SortDescriptions.Clear();
            SearchResultsView.SortDescriptions.Add(new SortDescription(primaryProperty, SearchResultSortDirection));
            if (primaryProperty != nameof(TmdbUnifiedSearchResult.Title))
            {
                SearchResultsView.SortDescriptions.Add(new SortDescription(nameof(TmdbUnifiedSearchResult.Title), ListSortDirection.Ascending));
            }
        }
    }

    private string GetSortIndicator(SearchResultSortColumn column)
    {
        if (SelectedSearchResultSortColumn != column)
        {
            return string.Empty;
        }

        return SearchResultSortDirection == ListSortDirection.Ascending ? "Asc" : "Desc";
    }

    private void NotifySearchResultSortIndicatorsChanged()
    {
        OnPropertyChanged(nameof(NameSortIndicator));
        OnPropertyChanged(nameof(TypeSortIndicator));
        OnPropertyChanged(nameof(YearSortIndicator));
        OnPropertyChanged(nameof(StatsSortIndicator));
        OnPropertyChanged(nameof(StatusSortIndicator));
    }

    private void ApplyMediaCardSort()
    {
        var sorted = MediaSortMode switch
        {
            MediaCardSortMode.TypeThenTitle => _allMediaCards
                .OrderBy(card => card.MediaKind)
                .ThenBy(card => card.Title),
            MediaCardSortMode.Title => _allMediaCards.OrderBy(card => card.Title),
            _ => _allMediaCards.OrderByDescending(card => card.CreatedUtc)
        };

        ExistingMediaCards.Clear();
        foreach (var card in sorted)
        {
            ExistingMediaCards.Add(card);
        }

        OnPropertyChanged(nameof(HasExistingMedia));
    }

    private bool CanSearch()
    {
        return !string.IsNullOrWhiteSpace(SearchText);
    }

    private bool CanCancelSearch()
    {
        return IsSearching;
    }

    private bool CanAddSelectedToLibrary()
    {
        return SelectedResult is not null && !SelectedResult.IsAlreadyAdded && !IsAddingToLibrary;
    }

    private void LoadRecipes()
    {
        _allRecipes = _recipeService.GetRecipes();
    }

    private IReadOnlyList<SearchRecipe> GetRecipesFor(MediaKind kind)
    {
        return _allRecipes
            .Where(recipe => recipe.TargetKind == kind)
            .OrderBy(recipe => recipe.Name)
            .ToList();
    }

    private string? GetDefaultRecipeId(MediaKind kind)
    {
        return _recipeService.GetDefaultRecipe(kind).RecipeId;
    }

    private async Task<(IReadOnlyList<TmdbShowSearchResult> Results, string? ErrorMessage)> SearchShowsLightweightSafelyAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await _showCatalogService.SearchTvShowsLightweightAsync(query, cancellationToken), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"TMDB show search failed: {ex.Message}", LogTarget.All);
            return ([], $"show search failed: {ex.Message}");
        }
    }

    private async Task<(IReadOnlyList<TmdbMovieSearchResult> Results, string? ErrorMessage)> SearchMoviesSafelyAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await _movieCatalogService.SearchMoviesAsync(query, cancellationToken), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"TMDB movie search failed: {ex.Message}", LogTarget.All);
            return ([], $"movie search failed: {ex.Message}");
        }
    }

    private static string BuildSearchFailureMessage(string? showError, string? movieError)
    {
        var errors = new[] { showError, movieError }
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Distinct()
            .ToList();
        return errors.Count == 0
            ? "TMDB search failed before results could be loaded."
            : $"TMDB search failed before results could be loaded. {string.Join(" ", errors)}";
    }

    private static string BuildSearchWarningSuffix(string? showError, string? movieError)
    {
        var errors = new[] { showError, movieError }
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .ToList();
        return errors.Count == 0 ? string.Empty : $" Partial results: {string.Join(", ", errors)}.";
    }
}
