using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class TorrentWorkspaceViewModel : ViewModelBase
{
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly IMediaCardCatalogService _mediaCardCatalogService;
    private readonly IPosterImageService _posterImageService;
    private readonly ITorrentCartService _torrentCartService;
    private readonly IAutomationFlowService _automationFlowService;
    private readonly IFetchJobService _fetchJobService;
    private readonly IDatabaseService _databaseService;
    private readonly IRecipeService _recipeService;
    private readonly IQbittorrentClient _qbittorrentClient;

    private IReadOnlyList<LibraryMediaCardViewModel> _allMediaCards = [];
    private bool _isLoadingRecipeAssignment;
    private CancellationTokenSource? _runCartCts;

    public TorrentWorkspaceViewModel(
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        IMediaCardCatalogService mediaCardCatalogService,
        IPosterImageService posterImageService,
        ITorrentCartService torrentCartService,
        IAutomationFlowService automationFlowService,
        IFetchJobService fetchJobService,
        IDatabaseService databaseService,
        IRecipeService recipeService,
        IQbittorrentClient qbittorrentClient)
    {
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _mediaCardCatalogService = mediaCardCatalogService;
        _posterImageService = posterImageService;
        _torrentCartService = torrentCartService;
        _automationFlowService = automationFlowService;
        _fetchJobService = fetchJobService;
        _databaseService = databaseService;
        _recipeService = recipeService;
        _qbittorrentClient = qbittorrentClient;

        _torrentCartService.CartChanged += (_, _) => OnCartChanged();
        _recipeService.RecipesChanged += (_, _) => LoadRecipeAssignment(SelectedMediaCard);
        RefreshWorkspace();
        StatusMessage = "Select a media card to view its cart.";
    }

    public ObservableCollection<LibraryMediaCardViewModel> MediaCards { get; } = [];

    public ObservableCollection<TorrentOrderViewModel> Orders { get; } = [];

    public ObservableCollection<SearchRecipe> EpisodeRecipeOptions { get; } = [];

    public ObservableCollection<SearchRecipe> PackRecipeOptions { get; } = [];

    public ObservableCollection<SearchRecipe> MovieRecipeOptions { get; } = [];

    [ObservableProperty]
    private LibraryMediaCardViewModel? selectedMediaCard;

    [ObservableProperty]
    private ImageSource? selectedPosterImage;

    [ObservableProperty]
    private MediaCardSortMode mediaSortMode = MediaCardSortMode.DateAddedDesc;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private TorrentOrderViewModel? selectedOrder;

    [ObservableProperty]
    private bool isRunningCart;

    [ObservableProperty]
    private string? selectedEpisodeRecipeId;

    [ObservableProperty]
    private string? selectedPackRecipeId;

    [ObservableProperty]
    private string? selectedMovieRecipeId;

    public bool HasMedia => MediaCards.Count > 0;

    public bool HasSelectedMedia => SelectedMediaCard is not null;

    public bool HasOrders => Orders.Count > 0;

    public bool IsDateSortSelected => MediaSortMode == MediaCardSortMode.DateAddedDesc;

    public bool IsTypeSortSelected => MediaSortMode == MediaCardSortMode.TypeThenTitle;

    public bool IsNameSortSelected => MediaSortMode == MediaCardSortMode.Title;

    public string CartTitle => SelectedMediaCard is null
        ? "Cart"
        : $"{SelectedMediaCard.Title}'s Cart";

    public bool IsShowRecipePanel => SelectedMediaCard?.IsShow == true;

    public bool IsMovieRecipePanel => SelectedMediaCard?.MediaKind == MediaKind.Movie;

    [RelayCommand]
    private void RefreshWorkspace()
    {
        _trackedShowService.RefreshAvailability();
        _trackedMovieService.RefreshAvailability();

        var selectedId = SelectedMediaCard?.Id;
        var selectedKind = SelectedMediaCard?.MediaKind;

        _allMediaCards = _mediaCardCatalogService.LoadCards();
        ApplyMediaCardSort();

        if (selectedId is not null && selectedKind is not null)
        {
            SelectedMediaCard = MediaCards.FirstOrDefault(card => card.Id == selectedId && card.MediaKind == selectedKind);
        }

        SelectedMediaCard ??= MediaCards.FirstOrDefault();
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(HasAnyCartOrders));
        ClearAllCartsCommand.NotifyCanExecuteChanged();
        StatusMessage = MediaCards.Count == 0
            ? "No media in library. Add items from Find/Add, then build carts from Library."
            : $"Loaded {MediaCards.Count} media item(s).";
    }

    [RelayCommand]
    private void SetMediaSortMode(MediaCardSortMode mode)
    {
        MediaSortMode = mode;
    }

    [RelayCommand]
    private void SelectMediaCard(LibraryMediaCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        SelectedMediaCard = card;
    }

    [RelayCommand]
    private void ViewOrder(TorrentOrderViewModel? order)
    {
        if (order is null)
        {
            return;
        }

        SelectedOrder = order;
        StatusMessage = $"Order detail view is not wired yet: {order.Title}";
    }

    [RelayCommand(CanExecute = nameof(CanRunCart))]
    private async Task RunCart()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var orders = _torrentCartService.GetOrders(SelectedMediaCard.MediaKind, SelectedMediaCard.Id)
            .Where(order => order.Status is not TorrentOrderStatus.AddedToClient and not TorrentOrderStatus.Completed)
            .ToList();
        if (orders.Count == 0)
        {
            StatusMessage = "No runnable orders in this cart.";
            return;
        }

        _runCartCts?.Cancel();
        _runCartCts?.Dispose();
        _runCartCts = new CancellationTokenSource();
        var cancellationToken = _runCartCts.Token;

        IsRunningCart = true;
        var addedCount = 0;
        var noCandidateCount = 0;
        var failedCount = 0;
        var canceledCount = 0;
        var finalStatus = string.Empty;

        try
        {
            foreach (var order in orders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recipeLabel = order.EpisodeId is null && order.SeasonNumber is not null
                    ? GetRecipeName(SelectedPackRecipeId, MediaKind.TvSeasonPack)
                    : order.TargetKind == MediaKind.Movie
                        ? GetRecipeName(SelectedMovieRecipeId, MediaKind.Movie)
                        : GetRecipeName(SelectedEpisodeRecipeId, MediaKind.TvEpisode);
                _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Searching, $"Searching with recipe: {recipeLabel}");
                try
                {
                    var result = await RunOrderAsync(order, cancellationToken);
                    if (result.NoCandidates)
                    {
                        noCandidateCount++;
                        _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.NoCandidates, result.Detail);
                        continue;
                    }

                    addedCount++;
                    _torrentCartService.UpdateOrderStatus(
                        order.Id,
                        TorrentOrderStatus.AddedToClient,
                        result.Detail);
                }
                catch (OperationCanceledException)
                {
                    canceledCount++;
                    _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Canceled, "Stopped by user.");
                    break;
                }
                catch (NotSupportedException ex)
                {
                    failedCount++;
                    _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Failed, ex.Message);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Failed, ex.Message);
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                _trackedShowService.RefreshAvailability();
                _trackedMovieService.RefreshAvailability();
                finalStatus = $"Run complete. Added={addedCount}, No candidates={noCandidateCount}, Failed={failedCount}.";
            }
            else
            {
                finalStatus = canceledCount > 0
                    ? $"Cart run stopped. Added={addedCount}, Canceled={canceledCount}, Failed={failedCount}."
                    : "Cart run stopped.";
            }
        }
        catch (OperationCanceledException)
        {
            finalStatus = "Cart run stopped.";
        }
        finally
        {
            _runCartCts?.Dispose();
            _runCartCts = null;
            IsRunningCart = false;
            _allMediaCards = _mediaCardCatalogService.LoadCards();
            ApplyMediaCardSort();
            if (SelectedMediaCard is not null)
            {
                await LoadSelectedCartAsync(SelectedMediaCard);
            }

            if (!string.IsNullOrWhiteSpace(finalStatus))
            {
                StatusMessage = finalStatus;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunningCart))]
    private void StopRunCart()
    {
        _runCartCts?.Cancel();
        StatusMessage = "Stopping cart run...";
    }

    [RelayCommand(CanExecute = nameof(HasOrders))]
    private void ClearCart()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Clear all orders in {SelectedMediaCard.Title}'s cart?",
            "Clear Cart",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var removed = _torrentCartService.ClearCart(SelectedMediaCard.MediaKind, SelectedMediaCard.Id);
        StatusMessage = removed == 0
            ? $"{SelectedMediaCard.Title}'s cart is already empty."
            : $"Cleared {removed} order(s) from {SelectedMediaCard.Title}'s cart.";
    }

    [RelayCommand(CanExecute = nameof(HasAnyCartOrders))]
    private void ClearAllCarts()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Clear all carts for every media item?",
            "Clear All Carts",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var removed = _torrentCartService.ClearAllCarts();
        StatusMessage = removed == 0
            ? "No cart orders to clear."
            : $"Cleared {removed} order(s) from all carts.";
    }

    [RelayCommand]
    private void RemoveOrder(TorrentOrderViewModel? order)
    {
        if (order is null)
        {
            return;
        }

        _torrentCartService.RemoveOrder(order.Id);
        StatusMessage = $"Removed order '{order.Title}'.";
    }

    [RelayCommand]
    private void GoToLibraryHint()
    {
        StatusMessage = "Use the Library tab to add episodes, movies, or season packs to cart.";
    }

    public bool HasAnyCartOrders => MediaCards.Any(card => card.OrderCount > 0);

    partial void OnSelectedMediaCardChanged(LibraryMediaCardViewModel? value)
    {
        foreach (var card in MediaCards)
        {
            card.IsSelected = ReferenceEquals(card, value);
        }

        _ = LoadSelectedCartAsync(value);
        LoadRecipeAssignment(value);
        OnPropertyChanged(nameof(HasSelectedMedia));
        OnPropertyChanged(nameof(CartTitle));
        OnPropertyChanged(nameof(IsShowRecipePanel));
        OnPropertyChanged(nameof(IsMovieRecipePanel));
    }

    partial void OnSelectedEpisodeRecipeIdChanged(string? value)
    {
        if (_isLoadingRecipeAssignment || SelectedMediaCard?.IsShow != true || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _trackedShowService.UpdateRecipe(SelectedMediaCard.Id, value);
        StatusMessage = $"Episode recipe set to {GetRecipeName(value, MediaKind.TvEpisode)}.";
    }

    partial void OnSelectedPackRecipeIdChanged(string? value)
    {
        if (_isLoadingRecipeAssignment || SelectedMediaCard?.IsShow != true || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _trackedShowService.UpdatePackRecipe(SelectedMediaCard.Id, value);
        StatusMessage = $"Pack recipe set to {GetRecipeName(value, MediaKind.TvSeasonPack)}.";
    }

    partial void OnSelectedMovieRecipeIdChanged(string? value)
    {
        if (_isLoadingRecipeAssignment || SelectedMediaCard?.MediaKind != MediaKind.Movie || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _trackedMovieService.UpdateRecipe(SelectedMediaCard.Id, value);
        StatusMessage = $"Movie recipe set to {GetRecipeName(value, MediaKind.Movie)}.";
    }

    partial void OnMediaSortModeChanged(MediaCardSortMode value)
    {
        ApplyMediaCardSort();
        OnPropertyChanged(nameof(IsDateSortSelected));
        OnPropertyChanged(nameof(IsTypeSortSelected));
        OnPropertyChanged(nameof(IsNameSortSelected));
    }

    partial void OnIsRunningCartChanged(bool value)
    {
        RunCartCommand.NotifyCanExecuteChanged();
        StopRunCartCommand.NotifyCanExecuteChanged();
        ClearCartCommand.NotifyCanExecuteChanged();
        ClearAllCartsCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadSelectedCartAsync(LibraryMediaCardViewModel? card)
    {
        Orders.Clear();
        SelectedPosterImage = null;
        SelectedOrder = null;

        if (card is null)
        {
            NotifyCartStateChanged();
            return;
        }

        SelectedPosterImage = await _posterImageService.LoadAsync(card.PosterPath);
        foreach (var order in _torrentCartService.GetOrders(card.MediaKind, card.Id))
        {
            Orders.Add(MapOrder(order));
        }

        NotifyCartStateChanged();
        StatusMessage = HasOrders
            ? $"{card.Title}'s cart has {Orders.Count} order(s)."
            : $"{card.Title}'s cart is empty. Add items from the Library tab.";
    }

    private void OnCartChanged()
    {
        _allMediaCards = _mediaCardCatalogService.LoadCards();
        ApplyMediaCardSort();
        if (SelectedMediaCard is not null)
        {
            _ = LoadSelectedCartAsync(SelectedMediaCard);
        }

        OnPropertyChanged(nameof(HasAnyCartOrders));
        ClearAllCartsCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCartStateChanged()
    {
        OnPropertyChanged(nameof(HasOrders));
        OnPropertyChanged(nameof(HasAnyCartOrders));
        RunCartCommand.NotifyCanExecuteChanged();
        ClearCartCommand.NotifyCanExecuteChanged();
        ClearAllCartsCommand.NotifyCanExecuteChanged();
    }

    private static TorrentOrderViewModel MapOrder(TorrentCartOrder order) =>
        new()
        {
            Id = order.Id,
            TargetKind = order.TargetKind,
            Title = order.Title,
            Summary = order.Summary,
            Status = order.Status,
            StatusDetail = order.StatusDetail
        };

    private void LoadRecipeAssignment(LibraryMediaCardViewModel? card)
    {
        _isLoadingRecipeAssignment = true;
        try
        {
            EpisodeRecipeOptions.Clear();
            PackRecipeOptions.Clear();
            MovieRecipeOptions.Clear();
            SelectedEpisodeRecipeId = null;
            SelectedPackRecipeId = null;
            SelectedMovieRecipeId = null;

            if (card is null)
            {
                return;
            }

            foreach (var recipe in _recipeService.GetRecipes().Where(recipe => recipe.TargetKind == MediaKind.TvEpisode).OrderBy(recipe => recipe.Name))
            {
                EpisodeRecipeOptions.Add(recipe);
            }

            foreach (var recipe in _recipeService.GetRecipes().Where(recipe => recipe.TargetKind == MediaKind.TvSeasonPack).OrderBy(recipe => recipe.Name))
            {
                PackRecipeOptions.Add(recipe);
            }

            foreach (var recipe in _recipeService.GetRecipes().Where(recipe => recipe.TargetKind == MediaKind.Movie).OrderBy(recipe => recipe.Name))
            {
                MovieRecipeOptions.Add(recipe);
            }

            if (card.IsShow)
            {
                var show = _databaseService.GetTrackedShow(card.Id);
                SelectedEpisodeRecipeId = show?.RecipeId ?? _recipeService.GetDefaultRecipe(MediaKind.TvEpisode).RecipeId;
                SelectedPackRecipeId = show?.PackRecipeId ?? _recipeService.GetDefaultRecipe(MediaKind.TvSeasonPack).RecipeId;
                return;
            }

            var movie = _databaseService.GetTrackedMovie(card.Id);
            SelectedMovieRecipeId = movie?.RecipeId ?? _recipeService.GetDefaultRecipe(MediaKind.Movie).RecipeId;
        }
        finally
        {
            _isLoadingRecipeAssignment = false;
        }
    }

    private string GetRecipeName(string? recipeId, MediaKind targetKind)
    {
        return _recipeService.GetRecipeOrDefault(recipeId, targetKind).Name;
    }

    private async Task<CartRunResult> RunOrderAsync(TorrentCartOrder order, CancellationToken cancellationToken)
    {
        if (order.TargetKind == MediaKind.Movie)
        {
            var result = await _automationFlowService.RunNowAsync(new RecipeRunRequest
            {
                TargetKind = MediaKind.Movie,
                MovieId = order.MediaId,
                RecipeId = SelectedMovieRecipeId
            }, cancellationToken);
            return result.BestCandidate is null
                ? CartRunResult.NoneFound(result.Summary)
                : CartRunResult.Added($"Added best candidate: {result.BestCandidate.DisplayName}");
        }

        if (order.EpisodeId is null || order.SeasonNumber is null || order.EpisodeNumber is null)
        {
            return await RunSeasonPackOrderAsync(order, cancellationToken);
        }

        var episodeResult = await _automationFlowService.RunNowAsync(new RecipeRunRequest
        {
            TargetKind = MediaKind.TvEpisode,
            ShowId = order.MediaId,
            SeasonNumber = order.SeasonNumber,
            EpisodeNumber = order.EpisodeNumber,
            RecipeId = SelectedEpisodeRecipeId
        }, cancellationToken);
        return episodeResult.BestCandidate is null
            ? CartRunResult.NoneFound(episodeResult.Summary)
            : CartRunResult.Added($"Added best candidate: {episodeResult.BestCandidate.DisplayName}");
    }

    private bool CanRunCart()
    {
        return HasOrders && !IsRunningCart;
    }

    private async Task<CartRunResult> RunSeasonPackOrderAsync(TorrentCartOrder order, CancellationToken cancellationToken)
    {
        if (order.SeasonNumber is null)
        {
            throw new InvalidOperationException("Season pack order is missing a season number.");
        }

        await _fetchJobService.FetchSeasonPacksAsync(order.MediaId, [order.SeasonNumber.Value], cancellationToken);
        if (!_fetchJobService.TryGetPackCandidates(order.MediaId, order.SeasonNumber.Value, out var candidates) ||
            candidates.Count == 0)
        {
            return CartRunResult.NoneFound("No season pack candidates found.");
        }

        var candidate = candidates
            .OrderByDescending(item => item.TotalScore)
            .ThenByDescending(item => item.Seeders)
            .First();
        var show = _databaseService.GetTrackedShow(order.MediaId)
            ?? throw new InvalidOperationException("Tracked show was not found.");
        var recipe = _recipeService.GetRecipeOrDefault(show.PackRecipeId ?? SelectedPackRecipeId, MediaKind.TvSeasonPack);
        var addModule = recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.AddTorrent && module.IsEnabled);
        var season = _databaseService.GetTrackedSeasons(order.MediaId)
            .FirstOrDefault(item => item.SeasonNumber == order.SeasonNumber.Value);
        var addedTorrent = await _qbittorrentClient.AddTorrentAsync(new AddTorrentRequest
        {
            Url = candidate.FileUrl,
            PluginName = candidate.PluginName,
            SavePath = season?.DownloadFolder ?? addModule?.SavePath ?? string.Empty,
            Category = addModule?.TorrentCategory ?? "AutoTorrent",
            Tags = string.IsNullOrWhiteSpace(addModule?.Tags) ? "media-manager" : addModule.Tags,
            Paused = addModule?.Paused ?? false
        });
        _trackedShowService.UpdateSeasonSelectedPack(order.MediaId, order.SeasonNumber.Value, candidate);
        _trackedShowService.UpdateSeasonPackTorrent(order.MediaId, order.SeasonNumber.Value, addedTorrent);
        return CartRunResult.Added($"Added season pack candidate: {candidate.DisplayName}");
    }

    private sealed record CartRunResult(bool AddedToClient, bool NoCandidates, string Detail)
    {
        public static CartRunResult Added(string detail) => new(true, false, detail);

        public static CartRunResult NoneFound(string detail) => new(false, true, detail);
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

        var selectedId = SelectedMediaCard?.Id;
        var selectedKind = SelectedMediaCard?.MediaKind;

        MediaCards.Clear();
        foreach (var card in sorted)
        {
            card.IsSelected = selectedId == card.Id && selectedKind == card.MediaKind;
            MediaCards.Add(card);
        }

        if (selectedId is not null && selectedKind is not null)
        {
            SelectedMediaCard = MediaCards.FirstOrDefault(card => card.Id == selectedId && card.MediaKind == selectedKind);
        }
    }
}
