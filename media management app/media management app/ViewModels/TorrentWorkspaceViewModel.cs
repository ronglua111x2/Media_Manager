using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.Views;

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
    private readonly ISettingsService _settingsService;
    private readonly ITorrentReconciliationService _torrentReconciliationService;
    private readonly IPackLinkCoordinatorService _packLinkCoordinatorService;
    private readonly ITorrentAddDiskAssignmentService _torrentAddDiskAssignmentService;
    private readonly IDownloadFolderCatalogService _downloadFolderCatalogService;
    private readonly IAppLogger _logger;
    private readonly IQbittorrentViewerService _qbittorrentViewerService;

    private IReadOnlyList<LibraryMediaCardViewModel> _allMediaCards = [];
    private bool _isLoadingRecipeAssignment;
    private bool _isRestoringTorrentUiState;
    private long? _pendingRestoreMediaId;
    private MediaKind? _pendingRestoreMediaKind;
    private CancellationTokenSource? _operationCts;

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
        IQbittorrentClient qbittorrentClient,
        ISettingsService settingsService,
        ITorrentReconciliationService torrentReconciliationService,
        IPackLinkCoordinatorService packLinkCoordinatorService,
        ITorrentAddDiskAssignmentService torrentAddDiskAssignmentService,
        IDownloadFolderCatalogService downloadFolderCatalogService,
        IAppLogger logger,
        IAppLifecycleService lifecycleService,
        IQbittorrentViewerService qbittorrentViewerService)
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
        _settingsService = settingsService;
        _torrentReconciliationService = torrentReconciliationService;
        _packLinkCoordinatorService = packLinkCoordinatorService;
        _torrentAddDiskAssignmentService = torrentAddDiskAssignmentService;
        _downloadFolderCatalogService = downloadFolderCatalogService;
        _logger = logger;
        _qbittorrentViewerService = qbittorrentViewerService;
        _qbittorrentViewerService.IsOpenChanged += (_, _) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsQbittorrentViewerOpen = _qbittorrentViewerService.IsOpen;
            });
        };

        _torrentCartService.CartChanged += (_, _) => OnCartChanged();
        _torrentReconciliationService.Reconciled += (_, _) => RefreshOrdersFromReconcile();
        _packLinkCoordinatorService.PackReconciled += (_, _) => RefreshOrdersFromReconcile();
        _recipeService.RecipesChanged += (_, _) => LoadRecipeAssignment(SelectedMediaCard);
        lifecycleService.AppModeChanged += OnAppModeChanged;
        RestoreTorrentUiState();
        LoadWorkspaceCards();
        OpenQbittorrentCommand.NotifyCanExecuteChanged();
        StatusMessage = "Select a media card to view its cart.";
    }

    public ObservableCollection<LibraryMediaCardViewModel> MediaCards { get; } = [];

    public ObservableCollection<TorrentOrderViewModel> Orders { get; } = [];

    public ObservableCollection<SearchRecipe> EpisodeRecipeOptions { get; } = [];

    public ObservableCollection<SearchRecipe> PackRecipeOptions { get; } = [];

    public ObservableCollection<SearchRecipe> MovieRecipeOptions { get; } = [];

    public IReadOnlyList<WatchStatusFilterOption> WatchStatusFilterOptions { get; } =
    [
        new() { Status = null, Label = "All statuses" },
        new() { Status = UserWatchStatus.None, Label = "Unset" },
        new() { Status = UserWatchStatus.Watching, Label = "Watching" },
        new() { Status = UserWatchStatus.Completed, Label = "Completed" },
        new() { Status = UserWatchStatus.OnHold, Label = "On-Hold" },
        new() { Status = UserWatchStatus.Dropped, Label = "Dropped" },
        new() { Status = UserWatchStatus.PlanToWatch, Label = "Plan to Watch" }
    ];

    [ObservableProperty]
    private LibraryMediaCardViewModel? selectedMediaCard;

    [ObservableProperty]
    private ImageSource? selectedPosterImage;

    [ObservableProperty]
    private MediaCardSortMode mediaSortMode = MediaCardSortMode.DateAddedDesc;

    [ObservableProperty]
    private string mediaSearchQuery = string.Empty;

    [ObservableProperty]
    private WatchStatusFilterOption? selectedWatchStatusFilter;

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

    [ObservableProperty]
    private int maxPackCandidates = 10;

    public bool HasMedia => MediaCards.Count > 0;

    public bool HasLibraryMedia => _allMediaCards.Count > 0;

    public bool HasNoFilterMatches => HasLibraryMedia && !HasMedia;

    public bool HasSelectedMedia => SelectedMediaCard is not null;

    public bool HasOrders => Orders.Count > 0;

    public bool HasAddableOrders => Orders.Any(order => order.CanAddToClient);

    public bool HasAcceptableCandidates => Orders.Any(order => order.CanAccept);

    public bool HasCandidatesInCart => Orders.Any(order => order.HasCandidates);

    public bool IsDateSortSelected => MediaSortMode == MediaCardSortMode.DateAddedDesc;

    public bool IsTypeSortSelected => MediaSortMode == MediaCardSortMode.TypeThenTitle;

    public bool IsNameSortSelected => MediaSortMode == MediaCardSortMode.Title;

    public string CartTitle => SelectedMediaCard is null
        ? "Cart"
        : $"{SelectedMediaCard.Title}'s Cart";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QbittorrentButtonText))]
    [NotifyPropertyChangedFor(nameof(QbittorrentButtonToolTip))]
    private bool isQbittorrentViewerOpen;

    public string QbittorrentButtonText => IsQbittorrentViewerOpen ? "Show" : "Open";

    public string QbittorrentButtonToolTip => IsQbittorrentViewerOpen
        ? "Show qBittorrent window"
        : "Open qBittorrent window";

    [RelayCommand(CanExecute = nameof(CanOpenQbittorrent))]
    private void OpenQbittorrent()
    {
        _qbittorrentViewerService.ShowOrActivate();
    }

    private bool CanOpenQbittorrent() => !string.IsNullOrWhiteSpace(GetQbittorrentWebUiUrl());

    private string GetQbittorrentWebUiUrl()
    {
        var url = _settingsService.Current.AutoTorrent?.QbittorrentWebUiUrl;
        return string.IsNullOrWhiteSpace(url)
            ? string.Empty
            : url.Trim();
    }

    public bool IsShowRecipePanel => SelectedMediaCard?.IsShow == true;

    public bool IsMovieRecipePanel => SelectedMediaCard?.MediaKind == MediaKind.Movie;

    [RelayCommand]
    private void RefreshWorkspace()
    {
        OpenQbittorrentCommand.NotifyCanExecuteChanged();
        _trackedShowService.RefreshAvailability();
        _trackedMovieService.RefreshAvailability();
        LoadWorkspaceCards();
    }

    private void LoadWorkspaceCards()
    {
        var selectedId = SelectedMediaCard?.Id;
        var selectedKind = SelectedMediaCard?.MediaKind;

        _allMediaCards = _mediaCardCatalogService.LoadCards();
        ApplyMediaCardFilterAndSort();

        if (selectedId is not null && selectedKind is not null)
        {
            SelectedMediaCard = MediaCards.FirstOrDefault(card => card.Id == selectedId && card.MediaKind == selectedKind);
        }

        TryRestorePendingSelectedMedia();
        SelectedMediaCard ??= MediaCards.FirstOrDefault();
        OnPropertyChanged(nameof(HasLibraryMedia));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(HasNoFilterMatches));
        OnPropertyChanged(nameof(HasAnyCartOrders));
        ClearAllCartsCommand.NotifyCanExecuteChanged();
        ClearCandidatesCommand.NotifyCanExecuteChanged();
        AcceptAllCandidatesCommand.NotifyCanExecuteChanged();
        StatusMessage = _allMediaCards.Count == 0
            ? "No media in library. Add items from Find/Add, then build carts from Library."
            : MediaCards.Count == 0
                ? "No media matches the current search/filter."
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
            .Where(order => order.Status is not TorrentOrderStatus.AddedToClient
                            and not TorrentOrderStatus.Downloading
                            and not TorrentOrderStatus.Completed)
            .ToList();
        if (orders.Count == 0)
        {
            StatusMessage = "No runnable orders in this cart.";
            return;
        }

        _torrentCartService.ClearCandidates(
            SelectedMediaCard.MediaKind,
            SelectedMediaCard.Id,
            orders.Select(order => order.Id));

        var cancellationToken = BeginOperation();
        var candidateCount = 0;
        var noCandidateCount = 0;
        var failedCount = 0;
        var canceledCount = 0;
        var finalStatus = string.Empty;

        try
        {
            var episodeOrders = orders
                .Where(order => order.TargetKind == MediaKind.TvEpisode && order.EpisodeId is not null)
                .ToList();
            if (episodeOrders.Count > 0)
            {
                try
                {
                    var episodeResult = await SearchEpisodeOrdersAsync(episodeOrders, cancellationToken);
                    candidateCount += episodeResult.CandidateCount;
                    noCandidateCount += episodeResult.NoCandidateCount;
                    failedCount += episodeResult.FailedCount;
                }
                catch (OperationCanceledException)
                {
                    MarkSearchingEpisodeOrdersCanceled(episodeOrders);
                    throw;
                }
            }

            var episodeOrderIds = episodeOrders.Select(order => order.Id).ToHashSet();
            foreach (var order in orders.Where(order => !episodeOrderIds.Contains(order.Id)))
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
                    var result = await SearchOrderAsync(order, cancellationToken);
                    if (result.NoCandidates)
                    {
                        noCandidateCount++;
                        _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.NoCandidates, result.Detail);
                        continue;
                    }

                    candidateCount++;
                    _torrentCartService.ReplaceCandidates(result.Order.Id, result.Candidates);
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
                finalStatus = $"Search complete. Candidates={candidateCount}, No candidates={noCandidateCount}, Failed={failedCount}.";
            }
            else
            {
                finalStatus = canceledCount > 0
                    ? $"Cart run stopped. Candidates={candidateCount}, Canceled={canceledCount}, Failed={failedCount}."
                    : "Cart run stopped.";
                _logger.Info(finalStatus, LogTarget.All);
            }
        }
        catch (OperationCanceledException)
        {
            finalStatus = "Cart run stopped.";
            _logger.Info(finalStatus, LogTarget.All);
        }
        finally
        {
            EndOperation();
            _allMediaCards = _mediaCardCatalogService.LoadCards();
            ApplyMediaCardFilterAndSort();
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
        _operationCts?.Cancel();
        StatusMessage = "Stopping current operation...";
        _logger.Info("Cart run stop requested by user.", LogTarget.All);
    }

    [RelayCommand(CanExecute = nameof(CanAddCart))]
    private async Task AddCart()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var orders = _torrentCartService.GetOrders(SelectedMediaCard.MediaKind, SelectedMediaCard.Id)
            .Where(order => order.HasSelectedCandidate &&
                            order.Status == TorrentOrderStatus.Approved)
            .ToList();
        if (orders.Count == 0)
        {
            StatusMessage = "No approved candidates are ready to add.";
            return;
        }

        var plan = _torrentAddDiskAssignmentService.BuildPlan(orders, GetSeasonDownloadFolder);
        _torrentAddDiskAssignmentService.AutoAssign(plan);

        var dialog = new TorrentAddDiskDialog(plan, _torrentAddDiskAssignmentService, _downloadFolderCatalogService)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true)
        {
            StatusMessage = "Add canceled.";
            return;
        }

        var cancellationToken = BeginOperation();
        var addedCount = 0;
        var failedCount = 0;
        var canceledCount = 0;
        try
        {
            foreach (var seasonGroup in plan.Rows
                         .Where(row => row.SeasonNumber is not null && row.TargetKind != MediaKind.Movie)
                         .GroupBy(row => (row.MediaId, row.SeasonNumber)))
            {
                var folder = seasonGroup.First().SelectedDownloadFolder;
                _trackedShowService.UpdateSeasonDownloadFolder(
                    seasonGroup.Key.MediaId,
                    seasonGroup.Key.SeasonNumber!.Value,
                    folder);
            }

            var orderIndex = 0;
            foreach (var order in orders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                orderIndex++;
                StatusMessage = $"Adding {orderIndex}/{orders.Count}: {order.Title}...";

                try
                {
                    var savePath = plan.Rows.First(row => row.OrderId == order.Id).SelectedDownloadFolder;
                    await AddOrderToClientAsync(order, savePath, cancellationToken);
                    addedCount++;
                }
                catch (OperationCanceledException)
                {
                    canceledCount++;
                    _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Canceled, "Add stopped by user.");
                    break;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Failed, ex.Message);
                }
            }

            _trackedShowService.RefreshAvailability();
            _trackedMovieService.RefreshAvailability();
            StatusMessage = canceledCount > 0
                ? $"Add stopped. Added={addedCount}, Failed={failedCount}, Canceled={canceledCount}."
                : $"Add complete. Added={addedCount}, Failed={failedCount}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Add stopped by user.";
        }
        finally
        {
            EndOperation();
            if (SelectedMediaCard is not null)
            {
                await LoadSelectedCartAsync(SelectedMediaCard);
            }
        }
    }

    private async Task RetryAddOrderAsync(long orderId)
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var order = _torrentCartService.GetOrder(orderId);
        if (order is null ||
            order.Status != TorrentOrderStatus.Failed ||
            !order.HasSelectedCandidate)
        {
            return;
        }

        var plan = _torrentAddDiskAssignmentService.BuildPlan([order], GetSeasonDownloadFolder);
        _torrentAddDiskAssignmentService.AutoAssign(plan);

        var dialog = new TorrentAddDiskDialog(plan, _torrentAddDiskAssignmentService, _downloadFolderCatalogService)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true)
        {
            StatusMessage = "Retry add canceled.";
            return;
        }

        var cancellationToken = BeginOperation();
        try
        {
            foreach (var seasonGroup in plan.Rows
                         .Where(row => row.SeasonNumber is not null && row.TargetKind != MediaKind.Movie)
                         .GroupBy(row => (row.MediaId, row.SeasonNumber)))
            {
                var folder = seasonGroup.First().SelectedDownloadFolder;
                _trackedShowService.UpdateSeasonDownloadFolder(
                    seasonGroup.Key.MediaId,
                    seasonGroup.Key.SeasonNumber!.Value,
                    folder);
            }

            StatusMessage = $"Retrying add: {order.Title}...";
            var savePath = plan.Rows.First(row => row.OrderId == order.Id).SelectedDownloadFolder;
            await AddOrderToClientAsync(order, savePath, cancellationToken);
            _trackedShowService.RefreshAvailability();
            _trackedMovieService.RefreshAvailability();
            StatusMessage = $"Added {order.Title} to qBittorrent.";
        }
        catch (OperationCanceledException)
        {
            _torrentCartService.UpdateOrderStatus(orderId, TorrentOrderStatus.Canceled, "Add stopped by user.");
            StatusMessage = "Retry add stopped by user.";
        }
        catch (Exception ex)
        {
            _torrentCartService.UpdateOrderStatus(orderId, TorrentOrderStatus.Failed, ex.Message);
            StatusMessage = $"Retry add failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
            await LoadSelectedCartAsync(SelectedMediaCard);
        }
    }

    private async Task RetrySearchOrderAsync(long orderId)
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var order = _torrentCartService.GetOrder(orderId);
        if (order is null ||
            order.Status is not (TorrentOrderStatus.Failed or TorrentOrderStatus.NoCandidates))
        {
            return;
        }

        _torrentCartService.ClearCandidates(SelectedMediaCard.MediaKind, SelectedMediaCard.Id, [orderId]);

        var cancellationToken = BeginOperation();
        try
        {
            if (order.TargetKind == MediaKind.TvEpisode && order.EpisodeId is not null)
            {
                try
                {
                    await SearchEpisodeOrdersAsync([order], cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    MarkSearchingEpisodeOrdersCanceled([order]);
                    throw;
                }
            }
            else
            {
                var recipeLabel = order.EpisodeId is null && order.SeasonNumber is not null
                    ? GetRecipeName(SelectedPackRecipeId, MediaKind.TvSeasonPack)
                    : order.TargetKind == MediaKind.Movie
                        ? GetRecipeName(SelectedMovieRecipeId, MediaKind.Movie)
                        : GetRecipeName(SelectedEpisodeRecipeId, MediaKind.TvEpisode);
                _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Searching, $"Searching with recipe: {recipeLabel}");

                try
                {
                    var result = await SearchOrderAsync(order, cancellationToken);
                    if (result.NoCandidates)
                    {
                        _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.NoCandidates, result.Detail);
                    }
                    else
                    {
                        _torrentCartService.ReplaceCandidates(result.Order.Id, result.Candidates);
                    }
                }
                catch (OperationCanceledException)
                {
                    _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Canceled, "Stopped by user.");
                    throw;
                }
                catch (Exception ex)
                {
                    _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Failed, ex.Message);
                }
            }

            _trackedShowService.RefreshAvailability();
            _trackedMovieService.RefreshAvailability();
            StatusMessage = $"Re-search complete for {order.Title}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Re-search stopped by user.";
        }
        finally
        {
            EndOperation();
            await LoadSelectedCartAsync(SelectedMediaCard);
        }
    }

    [RelayCommand(CanExecute = nameof(CanReconcileExistingTorrents))]
    private async Task ReconcileExistingTorrents()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var cancellationToken = BeginOperation();
        try
        {
            StatusMessage = $"Reconciling existing qBittorrent torrents for {SelectedMediaCard.Title}...";
            var scope = TorrentReconciliationScope.ForMedia(SelectedMediaCard.MediaKind, SelectedMediaCard.Id);
            var result = await _torrentReconciliationService.ReconcileAsync(scope, cancellationToken);
            await LoadSelectedCartAsync(SelectedMediaCard);
            StatusMessage = $"Torrent reconciliation complete. {result.Summary}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Reconciliation stopped by user.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Torrent reconciliation failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    private void RefreshOrdersFromReconcile()
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                if (SelectedMediaCard is not null)
                {
                    _ = LoadSelectedCartAsync(SelectedMediaCard);
                }
            });
    }

    public async Task ReconcilePackOrderAsync(long orderId)
    {
        var order = _torrentCartService.GetOrder(orderId);
        if (order is null || order.SeasonNumber is not int ownerSeason)
        {
            return;
        }

        var cancellationToken = BeginOperation();
        try
        {
            StatusMessage = $"Inspecting pack for {order.Title}...";
            var result = await _packLinkCoordinatorService.ReconcilePackAsync(
                order.MediaId,
                ownerSeason,
                PackLinkTrigger.Manual,
                cancellationToken);
            if (SelectedMediaCard is not null)
            {
                await LoadSelectedCartAsync(SelectedMediaCard);
            }

            StatusMessage = result.Skipped
                ? $"Pack inspect skipped: {result.SkipReason}"
                : result.Summary;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Pack reconcile stopped by user.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pack reconcile failed: {ex.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanAcceptAllCandidates))]
    private void AcceptAllCandidates()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var accepted = _torrentCartService.AcceptSelectedCandidates(SelectedMediaCard.MediaKind, SelectedMediaCard.Id);
        StatusMessage = accepted == 0
            ? "No selected candidates to accept."
            : $"Accepted {accepted} candidate(s).";
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

    [RelayCommand(CanExecute = nameof(HasCandidatesInCart))]
    private void ClearCandidates()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Clear all candidates in {SelectedMediaCard.Title}'s cart?",
            "Clear Candidates",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var cleared = _torrentCartService.ClearCandidates(SelectedMediaCard.MediaKind, SelectedMediaCard.Id);
        StatusMessage = cleared == 0
            ? $"{SelectedMediaCard.Title}'s cart has no candidates to clear."
            : $"Cleared candidates from {cleared} order(s) in {SelectedMediaCard.Title}'s cart.";
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

    private void SelectCandidate(long orderId, long candidateId)
    {
        _torrentCartService.SelectCandidate(orderId, candidateId);
        StatusMessage = "Candidate selection updated.";
    }

    private void AcceptCandidate(long orderId)
    {
        var order = _torrentCartService.GetOrder(orderId);
        if (order is null)
        {
            return;
        }

        if (!TryConfirmMultiSeasonPackAccept(order, out var conflictingOrders))
        {
            return;
        }

        _torrentCartService.AcceptSelectedCandidate(orderId);
        CancelSupersededPackOrders(order.MediaId, orderId, conflictingOrders);
        StatusMessage = conflictingOrders.Count > 0
            ? $"Candidate accepted. Canceled {conflictingOrders.Count} superseded pack order(s)."
            : "Candidate accepted.";
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
        PersistTorrentUiState();
        OnPropertyChanged(nameof(HasSelectedMedia));
        OnPropertyChanged(nameof(CartTitle));
        OnPropertyChanged(nameof(IsShowRecipePanel));
        OnPropertyChanged(nameof(IsMovieRecipePanel));
        ReconcileExistingTorrentsCommand.NotifyCanExecuteChanged();
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

    partial void OnMediaSearchQueryChanged(string value)
    {
        ApplyMediaCardFilterAndSort();
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(HasNoFilterMatches));
        PersistTorrentUiState();
    }

    partial void OnSelectedWatchStatusFilterChanged(WatchStatusFilterOption? value)
    {
        ApplyMediaCardFilterAndSort();
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(HasNoFilterMatches));
        PersistTorrentUiState();
    }

    partial void OnMediaSortModeChanged(MediaCardSortMode value)
    {
        ApplyMediaCardFilterAndSort();
        OnPropertyChanged(nameof(IsDateSortSelected));
        OnPropertyChanged(nameof(IsTypeSortSelected));
        OnPropertyChanged(nameof(IsNameSortSelected));
        PersistTorrentUiState();
    }

    partial void OnIsRunningCartChanged(bool value)
    {
        TorrentOrderViewModel.CartOperationRunning = value;
        foreach (var order in Orders)
        {
            order.NotifyOperationRunningChanged();
        }

        RunCartCommand.NotifyCanExecuteChanged();
        AddCartCommand.NotifyCanExecuteChanged();
        StopRunCartCommand.NotifyCanExecuteChanged();
        ReconcileExistingTorrentsCommand.NotifyCanExecuteChanged();
        ClearCartCommand.NotifyCanExecuteChanged();
        ClearAllCartsCommand.NotifyCanExecuteChanged();
        ClearCandidatesCommand.NotifyCanExecuteChanged();
        AcceptAllCandidatesCommand.NotifyCanExecuteChanged();
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

        SelectedPosterImage = await _posterImageService.LoadAsync(
            card.PosterPath,
            card.MediaKind,
            card.TmdbId);
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
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, OnCartChanged);
            return;
        }

        _allMediaCards = _mediaCardCatalogService.LoadCards();
        ApplyMediaCardFilterAndSort();
        if (SelectedMediaCard is not null)
        {
            _ = LoadSelectedCartAsync(SelectedMediaCard);
        }

        OnPropertyChanged(nameof(HasLibraryMedia));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(HasNoFilterMatches));
        OnPropertyChanged(nameof(HasAnyCartOrders));
        ClearAllCartsCommand.NotifyCanExecuteChanged();
        ClearCandidatesCommand.NotifyCanExecuteChanged();
        AcceptAllCandidatesCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCartStateChanged()
    {
        OnPropertyChanged(nameof(HasOrders));
        OnPropertyChanged(nameof(HasAnyCartOrders));
        OnPropertyChanged(nameof(HasAddableOrders));
        OnPropertyChanged(nameof(HasAcceptableCandidates));
        OnPropertyChanged(nameof(HasCandidatesInCart));
        RunCartCommand.NotifyCanExecuteChanged();
        AddCartCommand.NotifyCanExecuteChanged();
        ReconcileExistingTorrentsCommand.NotifyCanExecuteChanged();
        AcceptAllCandidatesCommand.NotifyCanExecuteChanged();
        ClearCartCommand.NotifyCanExecuteChanged();
        ClearAllCartsCommand.NotifyCanExecuteChanged();
        ClearCandidatesCommand.NotifyCanExecuteChanged();
    }

    private TorrentOrderViewModel MapOrder(TorrentCartOrder order)
    {
        var viewModel = new TorrentOrderViewModel
        {
            Id = order.Id,
            MediaId = order.MediaId,
            TargetKind = order.TargetKind,
            SeasonNumber = order.SeasonNumber,
            EpisodeId = order.EpisodeId,
            Title = order.Title,
            Summary = order.Summary,
            Status = order.Status,
            StatusDetail = order.StatusDetail,
            SelectedCandidateName = order.SelectedCandidateName,
            SelectedCandidateSeeders = order.SelectedCandidateSeeders,
            SelectedCandidateQuality = order.SelectedCandidateQuality,
            TorrentName = order.TorrentName,
            TorrentProgress = order.TorrentProgress
        };
        viewModel.CandidateSelected = SelectCandidate;
        viewModel.AcceptRequested = AcceptCandidate;
        viewModel.RetryAddRequested = orderId => _ = RetryAddOrderAsync(orderId);
        viewModel.RetrySearchRequested = orderId => _ = RetrySearchOrderAsync(orderId);
        viewModel.ReconcilePackRequested = orderId => _ = ReconcilePackOrderAsync(orderId);
        viewModel.LoadCandidates(_torrentCartService.GetCandidates(order.Id)
            .Select(candidate => new TorrentOrderCandidateViewModel(candidate)));
        return viewModel;
    }

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

    private async Task<CartRunResult> SearchEpisodeOrdersAsync(
        IReadOnlyList<TorrentCartOrder> orders,
        CancellationToken cancellationToken)
    {
        var result = new CartRunResult();
        var show = _databaseService.GetTrackedShow(orders[0].MediaId)
            ?? throw new InvalidOperationException("Tracked show was not found.");
        var recipe = _recipeService.GetRecipeOrDefault(SelectedEpisodeRecipeId ?? show.RecipeId, MediaKind.TvEpisode);
        var mode = RecipeRuntimeSettings.GetUseShowSnapshotSearch(recipe, _settingsService.Current.AutoTorrent)
            ? "Show snapshot search"
            : "Parallel episode search";
        _logger.Info(
            $"Run Cart: Media='{show.DisplayTitle}', Recipe='{recipe.Name}', Target='TV episode', Mode='{mode}', Orders={orders.Count}.",
            LogTarget.All);

        var ordersByEpisodeId = orders
            .Where(order => order.EpisodeId is not null)
            .GroupBy(order => order.EpisodeId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var order in ordersByEpisodeId.Values)
        {
            _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Searching, $"Queued for {mode} with recipe: {recipe.Name}");
        }

        var candidatesByEpisodeId = await _fetchJobService.FetchEpisodeCandidatesAsync(
            show.Id,
            ordersByEpisodeId.Keys.ToList(),
            recipe.RecipeId,
            (episodeId, detail) =>
            {
                if (ordersByEpisodeId.TryGetValue(episodeId, out var order))
                {
                    _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Searching, detail);
                }
            },
            cancellationToken);

        foreach (var (episodeId, order) in ordersByEpisodeId)
        {
            if (!candidatesByEpisodeId.TryGetValue(episodeId, out var candidates) || candidates.Count == 0)
            {
                result.NoCandidateCount++;
                _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.NoCandidates, "No candidates found.");
                continue;
            }

            result.CandidateCount++;
            _torrentCartService.ReplaceCandidates(order.Id, ToCartCandidates(candidates));
        }

        return result;
    }

    private async Task<CartSearchResult> SearchOrderAsync(TorrentCartOrder order, CancellationToken cancellationToken)
    {
        if (order.TargetKind == MediaKind.Movie)
        {
            var movie = _databaseService.GetTrackedMovie(order.MediaId)
                ?? throw new InvalidOperationException("Tracked movie was not found.");
            var recipe = _recipeService.GetRecipeOrDefault(SelectedMovieRecipeId ?? movie.RecipeId, MediaKind.Movie);
            _logger.Info(
                $"Run Cart: Media='{movie.DisplayTitle}', Recipe='{recipe.Name}', Target='Movie', Mode='Single movie search'.",
                LogTarget.All);
            var result = await _automationFlowService.DryRunAsync(new RecipeRunRequest
            {
                TargetKind = MediaKind.Movie,
                MovieId = order.MediaId,
                RecipeId = recipe.RecipeId
            }, cancellationToken);
            if (result.BestCandidate is null)
            {
                return CartSearchResult.NoneFound(order, result.Summary);
            }

            return CartSearchResult.Found(order, $"Found {result.AcceptedCandidates.Count} movie candidate(s).", ToCartCandidates(result.AcceptedCandidates));
        }

        if (order.EpisodeId is null || order.SeasonNumber is null || order.EpisodeNumber is null)
        {
            return await SearchSeasonPackOrderAsync(order, cancellationToken);
        }

        var episodeResult = await _automationFlowService.DryRunAsync(new RecipeRunRequest
        {
            TargetKind = MediaKind.TvEpisode,
            ShowId = order.MediaId,
            SeasonNumber = order.SeasonNumber,
            EpisodeNumber = order.EpisodeNumber,
            RecipeId = SelectedEpisodeRecipeId
        }, cancellationToken);
        if (episodeResult.BestCandidate is null)
        {
            return CartSearchResult.NoneFound(order, episodeResult.Summary);
        }

        return CartSearchResult.Found(order, $"Found {episodeResult.AcceptedCandidates.Count} episode candidate(s).", ToCartCandidates(episodeResult.AcceptedCandidates));
    }

    private CancellationToken BeginOperation()
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        IsRunningCart = true;
        return _operationCts.Token;
    }

    private void EndOperation()
    {
        _operationCts?.Dispose();
        _operationCts = null;
        IsRunningCart = false;
    }

    private void MarkSearchingEpisodeOrdersCanceled(IReadOnlyList<TorrentCartOrder> episodeOrders)
    {
        foreach (var order in episodeOrders)
        {
            var current = _torrentCartService.GetOrder(order.Id);
            if (current?.Status == TorrentOrderStatus.Searching)
            {
                _torrentCartService.UpdateOrderStatus(order.Id, TorrentOrderStatus.Canceled, "Stopped by user.");
            }
        }
    }

    private bool CanRunCart()
    {
        return HasOrders && !IsRunningCart;
    }

    private bool CanAddCart()
    {
        return HasAddableOrders && !IsRunningCart;
    }

    private bool CanReconcileExistingTorrents()
    {
        return SelectedMediaCard is not null && !IsRunningCart;
    }

    private bool CanAcceptAllCandidates()
    {
        return HasAcceptableCandidates && !IsRunningCart;
    }

    private async Task<CartSearchResult> SearchSeasonPackOrderAsync(TorrentCartOrder order, CancellationToken cancellationToken)
    {
        if (order.SeasonNumber is null)
        {
            throw new InvalidOperationException("Season pack order is missing a season number.");
        }

        var show = _databaseService.GetTrackedShow(order.MediaId)
            ?? throw new InvalidOperationException("Tracked show was not found.");
        var recipe = _recipeService.GetRecipeOrDefault(SelectedPackRecipeId ?? show.PackRecipeId, MediaKind.TvSeasonPack);
        _logger.Info(
            $"Run Cart: Media='{show.DisplayTitle}', Recipe='{recipe.Name}', Target='Season pack', Mode='Pack snapshot search', Season=S{order.SeasonNumber.Value:00}.",
            LogTarget.All);
        await _fetchJobService.FetchSeasonPacksAsync(
            order.MediaId,
            [order.SeasonNumber.Value],
            cancellationToken,
            Math.Clamp(MaxPackCandidates, 1, 50));
        if (!_fetchJobService.TryGetPackCandidates(order.MediaId, order.SeasonNumber.Value, out var candidates) ||
            candidates.Count == 0)
        {
            return CartSearchResult.NoneFound(order, "No season pack candidates found.");
        }

        return CartSearchResult.Found(order, $"Found {candidates.Count} season pack candidate(s).", ToCartCandidates(candidates));
    }

    private static IReadOnlyList<TorrentCartOrderCandidate> ToCartCandidates(IReadOnlyList<RecipeCandidateResult> candidates)
    {
        return candidates.Select((candidate, index) => new TorrentCartOrderCandidate
        {
            Rank = index + 1,
            IsSelected = index == 0,
            Name = candidate.SearchResult.FileName,
            Url = candidate.SearchResult.FileUrl,
            PluginName = candidate.SearchResult.EngineName,
            FileSize = candidate.SearchResult.FileSize,
            Seeders = candidate.SearchResult.Seeders,
            Leechers = candidate.SearchResult.Leechers,
            Quality = TorrentQuality.Detect(candidate.SearchResult.FileName),
            AudioCodec = string.Empty,
            CoveredSeasons = string.Empty,
            TotalScore = candidate.TotalScore
        }).ToList();
    }

    private static IReadOnlyList<TorrentCartOrderCandidate> ToCartCandidates(IReadOnlyList<EpisodeFetchCandidate> candidates)
    {
        return candidates.Select((candidate, index) => new TorrentCartOrderCandidate
        {
            Rank = index + 1,
            IsSelected = index == 0,
            Name = candidate.FileName,
            Url = candidate.FileUrl,
            PluginName = candidate.PluginName,
            FileSize = candidate.FileSize,
            Seeders = candidate.Seeders,
            Leechers = candidate.Leechers,
            Quality = candidate.QualityLabel,
            AudioCodec = candidate.AudioCodecLabel,
            CoveredSeasons = string.Empty,
            TotalScore = candidate.TotalScore
        }).ToList();
    }

    private static IReadOnlyList<TorrentCartOrderCandidate> ToCartCandidates(IReadOnlyList<SeasonPackCandidate> candidates)
    {
        return candidates.Select((candidate, index) => new TorrentCartOrderCandidate
        {
            Rank = index + 1,
            IsSelected = index == 0,
            Name = candidate.FileName,
            Url = candidate.FileUrl,
            PluginName = candidate.PluginName,
            FileSize = candidate.FileSize,
            Seeders = candidate.Seeders,
            Leechers = candidate.Leechers,
            Quality = candidate.QualityLabel,
            AudioCodec = candidate.AudioCodecLabel,
            CoveredSeasons = string.Join(",", candidate.CoveredSeasons),
            ContentProfileJson = candidate.ContentProfile is null
                ? string.Empty
                : PackContentProfile.Serialize(candidate.ContentProfile),
            TotalScore = candidate.TotalScore
        }).ToList();
    }

    private async Task AddOrderToClientAsync(TorrentCartOrder order, string savePath, CancellationToken cancellationToken = default)
    {
        _logger.Info(
            $"Adding torrent to qBittorrent. Order='{order.Title}', Url='{order.SelectedCandidateUrl}', SavePath='{savePath}'.",
            LogTarget.All);
        var addedTorrent = await _qbittorrentClient.AddTorrentAsync(CreateAddTorrentRequest(order, savePath), cancellationToken);
        order.TorrentHash = addedTorrent.Hash;
        order.TorrentName = addedTorrent.Name;
        order.TorrentState = QbittorrentTorrentStateNormalizer.Normalize(addedTorrent.State, addedTorrent.IsComplete);
        order.TorrentProgress = addedTorrent.Progress;
        order.Status = addedTorrent.IsComplete ? TorrentOrderStatus.Completed : TorrentOrderStatus.Downloading;
        order.StatusDetail = addedTorrent.IsComplete
            ? $"Ready to link: {addedTorrent.Name}"
            : $"Downloading: {addedTorrent.Name} ({addedTorrent.ProgressDisplay})";

        if (order.TargetKind == MediaKind.Movie)
        {
            var candidate = ToEpisodeCandidate(order);
            candidate.MovieId = order.MediaId;
            _trackedMovieService.UpdateSelectedCandidate(order.MediaId, candidate);
            _trackedMovieService.UpdateTorrentState(order.MediaId, addedTorrent);
            _torrentCartService.SaveOrder(order);
            return;
        }

        if (order.EpisodeId is null)
        {
            if (order.SeasonNumber is null)
            {
                throw new InvalidOperationException("Season pack order is missing a season number.");
            }

            var candidate = ToSeasonPackCandidate(order);
            var coveredSeasons = candidate.CoveredSeasons;
            if (coveredSeasons.Count > 0)
            {
                _trackedShowService.ClearSeasonSelectedPacksForSeasons(order.MediaId, coveredSeasons);
            }

            _trackedShowService.UpdateSeasonSelectedPack(order.MediaId, order.SeasonNumber.Value, candidate);
            _trackedShowService.UpdateSeasonPackTorrent(order.MediaId, order.SeasonNumber.Value, addedTorrent);
            CancelSupersededPackOrders(order.MediaId, order.SeasonNumber.Value, order.Id, coveredSeasons);
            _torrentCartService.SaveOrder(order);
            return;
        }

        var episodeCandidate = ToEpisodeCandidate(order);
        _trackedShowService.UpdateSelectedCandidate(order.EpisodeId.Value, episodeCandidate);
        _trackedShowService.UpdateTorrentState(order.EpisodeId.Value, addedTorrent);
        _torrentCartService.SaveOrder(order);
    }

    private AddTorrentRequest CreateAddTorrentRequest(TorrentCartOrder order, string savePath)
    {
        return new AddTorrentRequest
        {
            Url = order.SelectedCandidateUrl,
            PluginName = order.SelectedCandidatePlugin,
            SavePath = savePath,
            Category = _settingsService.Current.AutoTorrent.GetCategoryFor(order.TargetKind),
            Tags = "media-manager",
            Paused = false
        };
    }

    private string? GetSeasonDownloadFolder(long showId, int seasonNumber)
    {
        return _databaseService.GetTrackedSeasons(showId)
            .FirstOrDefault(season => season.SeasonNumber == seasonNumber)
            ?.DownloadFolder;
    }

    private string GetOrderDownloadFolder(TorrentCartOrder order)
    {
        if (order.TargetKind != MediaKind.Movie && order.SeasonNumber is not null)
        {
            var season = _databaseService.GetTrackedSeasons(order.MediaId)
                .FirstOrDefault(item => item.SeasonNumber == order.SeasonNumber.Value);
            if (!string.IsNullOrWhiteSpace(season?.DownloadFolder))
            {
                return season.DownloadFolder;
            }
        }

        return FirstNonEmpty(_settingsService.Current.AutoTorrent.DownloadFolder, _settingsService.Current.SourceFolders.FirstOrDefault());
    }

    private static void ApplyCandidate(TorrentCartOrder order, RecipeCandidateResult candidate)
    {
        order.SelectedCandidateName = candidate.SearchResult.FileName;
        order.SelectedCandidateUrl = candidate.SearchResult.FileUrl;
        order.SelectedCandidatePlugin = candidate.SearchResult.EngineName;
        order.SelectedCandidateFileSize = candidate.SearchResult.FileSize;
        order.SelectedCandidateSeeders = candidate.SearchResult.Seeders;
        order.SelectedCandidateLeechers = candidate.SearchResult.Leechers;
        order.SelectedCandidateQuality = TorrentQuality.Detect(candidate.SearchResult.FileName);
        order.SelectedCandidateAudioCodec = string.Empty;
        order.SelectedCandidateCoveredSeasons = string.Empty;
        order.SelectedCandidateTotalScore = candidate.TotalScore;
        order.TorrentHash = string.Empty;
        order.TorrentName = string.Empty;
        order.TorrentState = string.Empty;
        order.TorrentProgress = 0;
        order.Status = TorrentOrderStatus.CandidatesFound;
        order.StatusDetail = $"Candidate found: {candidate.DisplayName}";
    }

    private static void ApplyCandidate(TorrentCartOrder order, SeasonPackCandidate candidate)
    {
        order.SelectedCandidateName = candidate.FileName;
        order.SelectedCandidateUrl = candidate.FileUrl;
        order.SelectedCandidatePlugin = candidate.PluginName;
        order.SelectedCandidateFileSize = candidate.FileSize;
        order.SelectedCandidateSeeders = candidate.Seeders;
        order.SelectedCandidateLeechers = candidate.Leechers;
        order.SelectedCandidateQuality = candidate.QualityLabel;
        order.SelectedCandidateAudioCodec = candidate.AudioCodecLabel;
        order.SelectedCandidateCoveredSeasons = string.Join(",", candidate.CoveredSeasons);
        order.SelectedCandidateContentProfile = candidate.ContentProfile is null
            ? string.Empty
            : PackContentProfile.Serialize(candidate.ContentProfile);
        order.SelectedCandidateTotalScore = candidate.TotalScore;
        order.TorrentHash = string.Empty;
        order.TorrentName = string.Empty;
        order.TorrentState = string.Empty;
        order.TorrentProgress = 0;
        order.Status = TorrentOrderStatus.CandidatesFound;
        order.StatusDetail = $"Candidate found: {candidate.DisplayName}";
    }

    private static EpisodeFetchCandidate ToEpisodeCandidate(TorrentCartOrder order)
    {
        return new EpisodeFetchCandidate
        {
            EpisodeId = order.EpisodeId ?? 0,
            FileName = order.SelectedCandidateName,
            FileUrl = order.SelectedCandidateUrl,
            FileSize = order.SelectedCandidateFileSize,
            Seeders = order.SelectedCandidateSeeders,
            Leechers = order.SelectedCandidateLeechers,
            PluginName = order.SelectedCandidatePlugin,
            QualityLabel = order.SelectedCandidateQuality,
            AudioCodecLabel = order.SelectedCandidateAudioCodec,
            TotalScore = order.SelectedCandidateTotalScore
        };
    }

    private static SeasonPackCandidate ToSeasonPackCandidate(TorrentCartOrder order)
    {
        return new SeasonPackCandidate
        {
            ShowId = order.MediaId,
            OwnerSeasonNumber = order.SeasonNumber ?? 0,
            FileName = order.SelectedCandidateName,
            FileUrl = order.SelectedCandidateUrl,
            PluginName = order.SelectedCandidatePlugin,
            FileSize = order.SelectedCandidateFileSize,
            Seeders = order.SelectedCandidateSeeders,
            Leechers = order.SelectedCandidateLeechers,
            QualityLabel = order.SelectedCandidateQuality,
            AudioCodecLabel = order.SelectedCandidateAudioCodec,
            CoveredSeasons = ParseCoveredSeasons(order),
            ContentProfile = PackContentProfile.Deserialize(order.SelectedCandidateContentProfile),
            TotalScore = order.SelectedCandidateTotalScore
        };
    }

    private static IReadOnlyList<int> ParseCoveredSeasons(TorrentCartOrder order)
    {
        var seasons = order.SelectedCandidateCoveredSeasons
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
            .Where(value => value > 0)
            .Distinct()
            .Order()
            .ToList();

        if (seasons.Count == 0 && order.SeasonNumber is not null)
        {
            seasons.Add(order.SeasonNumber.Value);
        }

        return seasons;
    }

    private bool TryConfirmMultiSeasonPackAccept(TorrentCartOrder order, out IReadOnlyList<TorrentCartOrder> conflictingOrders)
    {
        conflictingOrders = [];
        if (!IsSeasonPackOrder(order) || order.SeasonNumber is not int ownerSeason)
        {
            return true;
        }

        var selected = _torrentCartService.GetCandidates(order.Id).FirstOrDefault(candidate => candidate.IsSelected);
        if (selected is null)
        {
            return true;
        }

        var coveredSeasons = ParseCoveredSeasonsFromValue(selected.CoveredSeasons, ownerSeason);
        if (coveredSeasons.Count <= 1)
        {
            return true;
        }

        conflictingOrders = GetConflictingPackOrders(order.MediaId, order.Id, ownerSeason, coveredSeasons);
        if (conflictingOrders.Count == 0)
        {
            return true;
        }

        var affectedOrders = string.Join("\n", conflictingOrders.Select(conflict => $"- {conflict.Title}"));
        var coveredDisplay = string.Join(", ", coveredSeasons.Select(season => $"S{season:00}"));
        var confirm = System.Windows.MessageBox.Show(
            $"This multi-season pack covers {coveredDisplay}.\n\nAccepting will cancel these cart pack orders:\n{affectedOrders}\n\nCovered seasons will be managed from S{ownerSeason:00} in Library.",
            "Multi-Season Pack",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return confirm == System.Windows.MessageBoxResult.Yes;
    }

    private void CancelSupersededPackOrders(long showId, long ownerOrderId, IReadOnlyList<TorrentCartOrder> ordersToCancel)
    {
        var ownerSeason = _torrentCartService.GetOrder(ownerOrderId)?.SeasonNumber;
        foreach (var conflict in ordersToCancel)
        {
            _torrentCartService.UpdateOrderStatus(
                conflict.Id,
                TorrentOrderStatus.Canceled,
                ownerSeason is int season
                    ? $"Superseded by multi-season pack on S{season:00}"
                    : "Superseded by multi-season pack");
        }
    }

    private void CancelSupersededPackOrders(
        long showId,
        int ownerSeason,
        long ownerOrderId,
        IReadOnlyList<int> coveredSeasons)
    {
        CancelSupersededPackOrders(
            showId,
            ownerOrderId,
            GetConflictingPackOrders(showId, ownerOrderId, ownerSeason, coveredSeasons));
    }

    private IReadOnlyList<TorrentCartOrder> GetConflictingPackOrders(
        long showId,
        long ownerOrderId,
        int ownerSeason,
        IReadOnlyList<int> coveredSeasons)
    {
        var conflictingSeasons = coveredSeasons
            .Where(season => season != ownerSeason)
            .ToHashSet();
        if (conflictingSeasons.Count == 0)
        {
            return [];
        }

        return _torrentCartService.GetOrders(MediaKind.TvEpisode, showId)
            .Where(order => order.Id != ownerOrderId &&
                            IsSeasonPackOrder(order) &&
                            order.SeasonNumber is int seasonNumber &&
                            conflictingSeasons.Contains(seasonNumber) &&
                            order.Status != TorrentOrderStatus.Canceled)
            .OrderBy(order => order.SeasonNumber)
            .ToList();
    }

    private static bool IsSeasonPackOrder(TorrentCartOrder order) =>
        order.EpisodeId is null && order.SeasonNumber is not null && order.TargetKind != MediaKind.Movie;

    private static IReadOnlyList<int> ParseCoveredSeasonsFromValue(string coveredSeasonsValue, int fallbackSeason)
    {
        var seasons = coveredSeasonsValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
            .Where(value => value > 0)
            .Distinct()
            .Order()
            .ToList();

        if (seasons.Count == 0)
        {
            seasons.Add(fallbackSeason);
        }

        return seasons;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private sealed class CartRunResult
    {
        public int CandidateCount { get; set; }

        public int NoCandidateCount { get; set; }

        public int FailedCount { get; set; }
    }

    private sealed record CartSearchResult(
        TorrentCartOrder Order,
        bool NoCandidates,
        string Detail,
        IReadOnlyList<TorrentCartOrderCandidate> Candidates)
    {
        public static CartSearchResult Found(
            TorrentCartOrder order,
            string detail,
            IReadOnlyList<TorrentCartOrderCandidate> candidates) =>
            new(order, false, detail, candidates);

        public static CartSearchResult NoneFound(TorrentCartOrder order, string detail) => new(order, true, detail, []);
    }

    private void ApplyMediaCardFilterAndSort()
    {
        IEnumerable<LibraryMediaCardViewModel> filtered = _allMediaCards;

        if (!string.IsNullOrWhiteSpace(MediaSearchQuery))
        {
            var query = MediaSearchQuery.Trim();
            filtered = filtered.Where(card =>
                card.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedWatchStatusFilter?.Status is { } statusFilter)
        {
            filtered = filtered.Where(card => card.WatchStatus == statusFilter);
        }

        var sorted = MediaSortMode switch
        {
            MediaCardSortMode.TypeThenTitle => filtered
                .OrderBy(card => card.MediaKind)
                .ThenBy(card => card.Title),
            MediaCardSortMode.Title => filtered.OrderBy(card => card.Title),
            _ => filtered.OrderByDescending(card => card.CreatedUtc)
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
            // Keep selection only when it still matches the filter. Do not auto-select a replacement.
            SelectedMediaCard = MediaCards.FirstOrDefault(card =>
                card.Id == selectedId && card.MediaKind == selectedKind);
        }

        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(HasNoFilterMatches));
    }

    private void RestoreTorrentUiState()
    {
        _isRestoringTorrentUiState = true;
        try
        {
            var ui = _settingsService.Current.Ui ?? new UiSettings();
            MediaSortMode = ui.TorrentMediaSortMode;
            MediaSearchQuery = ui.TorrentMediaSearchQuery ?? string.Empty;
            SelectedWatchStatusFilter = WatchStatusFilterOptions.FirstOrDefault(option =>
                option.Status == ui.TorrentWatchStatusFilter) ?? WatchStatusFilterOptions[0];
            _pendingRestoreMediaId = ui.TorrentSelectedMediaId;
            _pendingRestoreMediaKind = ui.TorrentSelectedMediaKind;
        }
        finally
        {
            _isRestoringTorrentUiState = false;
        }
    }

    private void TryRestorePendingSelectedMedia()
    {
        if (SelectedMediaCard is not null ||
            _pendingRestoreMediaId is null ||
            _pendingRestoreMediaKind is null)
        {
            _pendingRestoreMediaId = null;
            _pendingRestoreMediaKind = null;
            return;
        }

        var pendingId = _pendingRestoreMediaId;
        var pendingKind = _pendingRestoreMediaKind;
        _pendingRestoreMediaId = null;
        _pendingRestoreMediaKind = null;

        SelectedMediaCard = MediaCards.FirstOrDefault(card =>
            card.Id == pendingId && card.MediaKind == pendingKind);
    }

    private void PersistTorrentUiState()
    {
        if (_isRestoringTorrentUiState)
        {
            return;
        }

        var ui = _settingsService.Current.Ui ??= new UiSettings();
        var search = MediaSearchQuery ?? string.Empty;
        var filter = SelectedWatchStatusFilter?.Status;
        var selectedId = SelectedMediaCard?.Id;
        var selectedKind = SelectedMediaCard?.MediaKind;
        if (ui.TorrentMediaSortMode == MediaSortMode &&
            string.Equals(ui.TorrentMediaSearchQuery, search, StringComparison.Ordinal) &&
            ui.TorrentWatchStatusFilter == filter &&
            ui.TorrentSelectedMediaId == selectedId &&
            ui.TorrentSelectedMediaKind == selectedKind)
        {
            return;
        }

        ui.TorrentMediaSortMode = MediaSortMode;
        ui.TorrentMediaSearchQuery = search;
        ui.TorrentWatchStatusFilter = filter;
        ui.TorrentSelectedMediaId = selectedId;
        ui.TorrentSelectedMediaKind = selectedKind;
        _settingsService.Save();
    }

    private void OnAppModeChanged(object? sender, AppMode mode)
    {
        if (mode == AppMode.Background)
        {
            ReleasePosterMemory();
            return;
        }

        if (SelectedMediaCard is not null)
        {
            _ = ReloadPosterOnForegroundAsync();
        }
    }

    private void ReleasePosterMemory()
    {
        SelectedPosterImage = null;
        foreach (var card in MediaCards)
        {
            card.PosterImage = null;
        }
    }

    private async Task ReloadPosterOnForegroundAsync()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        SelectedPosterImage = await _posterImageService.LoadAsync(
            SelectedMediaCard.PosterPath,
            SelectedMediaCard.MediaKind,
            SelectedMediaCard.TmdbId);
    }
}
