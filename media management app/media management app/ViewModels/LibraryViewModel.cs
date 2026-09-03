using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.Services.Gemini;
using media_management_app.Views;
using WinForms = System.Windows.Forms;

namespace media_management_app.ViewModels;

public sealed partial class LibraryViewModel : ViewModelBase
{
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly IDatabaseService _databaseService;
    private readonly IMediaCardCatalogService _mediaCardCatalogService;
    private readonly IPosterImageService _posterImageService;
    private readonly ITorrentCartService _torrentCartService;
    private readonly ILibraryManagementService _libraryManagementService;
    private readonly IRecipeService _recipeService;
    private readonly IAutoTorrentLinkService _autoTorrentLinkService;
    private readonly ITorrentReconciliationService _torrentReconciliationService;
    private readonly IMediaImportService _mediaImportService;
    private readonly IMediaMetadataSyncService _mediaMetadataSyncService;
    private readonly ISettingsService _settingsService;
    private readonly IDownloadFolderCatalogService _downloadFolderCatalogService;
    private readonly IGeminiLinkConfirmationService _geminiLinkConfirmationService;

    private IReadOnlyList<LibraryMediaCardViewModel> _allMediaCards = [];
    private long? _loadedDetailMediaId;
    private MediaKind? _loadedDetailMediaKind;
    private bool _suppressSeriesStatusUpdate;
    private bool _suppressWatchProgressUpdate;
    private bool _suppressRatingThoughtUpdate;
    private int _watchProgressSuppressGeneration;

    private const int ThoughtMaxLength = 250;
    private const double RatingStep = 0.1;
    private const double RatingMin = 0.0;
    private const double RatingMax = 10.0;

    public LibraryViewModel(
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        IDatabaseService databaseService,
        IMediaCardCatalogService mediaCardCatalogService,
        IPosterImageService posterImageService,
        ITorrentCartService torrentCartService,
        ILibraryManagementService libraryManagementService,
        IRecipeService recipeService,
        IAutoTorrentLinkService autoTorrentLinkService,
        ITorrentReconciliationService torrentReconciliationService,
        IPackLinkCoordinatorService packLinkCoordinatorService,
        IMediaImportService mediaImportService,
        IMediaMetadataSyncService mediaMetadataSyncService,
        ISettingsService settingsService,
        IDownloadFolderCatalogService downloadFolderCatalogService,
        IGeminiLinkConfirmationService geminiLinkConfirmationService,
        IAppLifecycleService lifecycleService)
    {
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _databaseService = databaseService;
        _mediaCardCatalogService = mediaCardCatalogService;
        _posterImageService = posterImageService;
        _torrentCartService = torrentCartService;
        _libraryManagementService = libraryManagementService;
        _recipeService = recipeService;
        _autoTorrentLinkService = autoTorrentLinkService;
        _torrentReconciliationService = torrentReconciliationService;
        _mediaImportService = mediaImportService;
        _mediaMetadataSyncService = mediaMetadataSyncService;
        _settingsService = settingsService;
        _downloadFolderCatalogService = downloadFolderCatalogService;
        _geminiLinkConfirmationService = geminiLinkConfirmationService;

        _torrentCartService.CartChanged += (_, _) =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshCartStateOnSelectedDetail);
                return;
            }

            RefreshCartStateOnSelectedDetail();
        };
        _torrentReconciliationService.Reconciled += (_, _) => RunRefreshSelectedDetailAfterReconcileOnUiThread();
        packLinkCoordinatorService.PackReconciled += (_, _) => RunRefreshSelectedDetailAfterReconcileOnUiThread();
        lifecycleService.AppModeChanged += OnAppModeChanged;
        SubscribeWatchStatusFilterOptions();
        RestoreLibraryUiState();
        RefreshLibrary();
        StatusMessage = "Select a media card to view details.";
    }

    public override void OnNavigatedTo()
    {
        // Force detail reload even when the same card remains selected (04 stale-detail bug).
        _loadedDetailMediaId = null;
        _loadedDetailMediaKind = null;
        RefreshLibrary();
        _ = ReloadSelectedDetailAsync();
    }

    private bool _isRestoringLibraryUiState;
    private bool _suppressWatchStatusFilterApply;
    private long? _pendingRestoreMediaId;
    private MediaKind? _pendingRestoreMediaKind;

    public ObservableCollection<LibraryMediaCardViewModel> MediaCards { get; } = [];

    public ObservableCollection<string> ImportFolders { get; } = [];

    public ObservableCollection<MediaImportGroupViewModel> ImportGroups { get; } = [];

    public ObservableCollection<MediaImportGroupViewModel> ReadyImportGroups { get; } = [];

    public ObservableCollection<MediaImportGroupViewModel> NeedsReviewImportGroups { get; } = [];

    public ObservableCollection<MediaImportGroupViewModel> IgnoredImportGroups { get; } = [];

    public IReadOnlyList<MediaCardSortFieldOption> MediaSortFieldOptions { get; } = MediaCardSortFieldOption.All;

    public IReadOnlyList<WatchStatusOption> WatchStatusOptions { get; } =
    [
        new() { Status = UserWatchStatus.None, Label = "Unset" },
        new() { Status = UserWatchStatus.Watching, Label = "Watching" },
        new() { Status = UserWatchStatus.Completed, Label = "Completed" },
        new() { Status = UserWatchStatus.OnHold, Label = "On-Hold" },
        new() { Status = UserWatchStatus.Dropped, Label = "Dropped" },
        new() { Status = UserWatchStatus.PlanToWatch, Label = "Plan to Watch" }
    ];

    public IReadOnlyList<WatchStatusFilterOption> WatchStatusFilterOptions { get; } = WatchStatusFilterOption.CreateAll();

    [ObservableProperty]
    private LibraryMediaCardViewModel? selectedMediaCard;

    [ObservableProperty]
    private LibraryShowDetailViewModel? selectedShow;

    [ObservableProperty]
    private LibraryMovieDetailViewModel? selectedMovie;

    [ObservableProperty]
    private ShowSeriesStatus selectedShowSeriesStatus;

    [ObservableProperty]
    private UserWatchStatus selectedWatchStatus;

    [ObservableProperty]
    private int selectedWatchedEpisodes;

    [ObservableProperty]
    private int selectedWatchTotalEpisodes;

    [ObservableProperty]
    private double selectedRating;

    [ObservableProperty]
    private string selectedThought = string.Empty;

    [ObservableProperty]
    private bool isEditingThought;

    [ObservableProperty]
    private bool isThoughtPopupOpen;

    [ObservableProperty]
    private ImageSource? selectedPosterImage;

    [ObservableProperty]
    private MediaCardSortField mediaSortField = MediaCardSortField.DateAdded;

    [ObservableProperty]
    private bool isSortAscending;

    [ObservableProperty]
    private string mediaSearchQuery = string.Empty;

    [ObservableProperty]
    private string mediaSearchText = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isImportPanelOpen;

    [ObservableProperty]
    private bool isImportBusy;

    [ObservableProperty]
    private string importStatusMessage = "Add one or more folders to scan existing hardlinked media.";

    [ObservableProperty]
    private string? selectedImportFolder;

    [ObservableProperty]
    private bool showHiddenSeasons;

    public bool HasLibraryMedia => _allMediaCards.Count > 0;

    public bool HasMedia => MediaCards.Count > 0;

    public bool HasNoFilterMatches => HasLibraryMedia && !HasMedia;

    public bool HasSelectedMedia => SelectedMediaCard is not null;

    public bool IsSelectedShow => SelectedShow is not null;

    public bool IsSelectedMovie => SelectedMovie is not null;

    public bool ShowWatchEpisodeControls => IsSelectedShow;

    public string SelectedWatchEpisodesLabel =>
        $"Episodes: {SelectedWatchedEpisodes}/{SelectedWatchTotalEpisodes}";

    public bool CanIncrementWatchedEpisodes =>
        IsSelectedShow && SelectedWatchTotalEpisodes > 0 && SelectedWatchedEpisodes < SelectedWatchTotalEpisodes;

    public bool CanDecrementWatchedEpisodes =>
        IsSelectedShow && SelectedWatchedEpisodes > 0;

    public bool CanIncrementRating => HasSelectedMedia && SelectedRating < RatingMax;

    public bool CanDecrementRating => HasSelectedMedia && SelectedRating > RatingMin;

    public string SelectedRatingText
    {
        get => SelectedRating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                || double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out parsed))
            {
                SelectedRating = ClampRating(parsed);
                return;
            }

            OnPropertyChanged(nameof(SelectedRatingText));
        }
    }

    public bool ShowThoughtDisplay => HasSelectedMedia && !IsEditingThought;

    public bool ShowThoughtEditor => HasSelectedMedia && IsEditingThought;

    public bool HasThoughtText => !string.IsNullOrWhiteSpace(SelectedThought);

    public string ThoughtDisplayText =>
        string.IsNullOrWhiteSpace(SelectedThought) ? "Add a thought..." : SelectedThought;

    public bool ShowStopAutoTrackButton => IsSelectedShow && SelectedShow?.IsAutoTracked == true;

    public bool HasMediaSearchText => !string.IsNullOrEmpty(MediaSearchText);

    public bool HasAppliedMediaSearch => !string.IsNullOrWhiteSpace(MediaSearchQuery);

    public string MediaSortDirectionToolTip => MediaCardSort.GetDirectionToolTip(MediaSortField, IsSortAscending);

    public string WatchStatusFilterLabel => WatchStatusFilterOption.GetSummaryLabel(WatchStatusFilterOptions);

    public UserWatchStatus WatchStatusFilterLabelStatus
    {
        get
        {
            var selected = WatchStatusFilterOption.GetSelectedStatuses(WatchStatusFilterOptions);
            return selected.Count == 1 ? selected[0] : UserWatchStatus.None;
        }
    }

    public bool HasSelectedWatchStatusFilter => WatchStatusFilterOption.HasSelection(WatchStatusFilterOptions);

    private bool CanClearWatchStatusFilter() => HasSelectedWatchStatusFilter;

    public string SelectedShowSeriesStatusLabel => SelectedShowSeriesStatus switch
    {
        ShowSeriesStatus.Ongoing => "Ongoing",
        ShowSeriesStatus.Finished => "Finished",
        _ => "Unknown"
    };

    public bool HasImportFolders => ImportFolders.Count > 0;

    public bool HasImportGroups => ImportGroups.Count > 0;

    public bool CanScanImportFolders => HasImportFolders && !IsImportBusy;

    public bool CanImportSelected =>
        !IsImportBusy &&
        ImportGroups.Any(group => group.CanImport);

    public string ShowHiddenSeasonsButtonLabel => ShowHiddenSeasons
        ? "Hide hidden seasons"
        : $"Show hidden seasons ({SelectedShow?.HiddenSeasonCount ?? 0})";

    [RelayCommand]
    private void RefreshLibrary()
    {
        _trackedShowService.RefreshAvailability();
        _trackedMovieService.RefreshAvailability();

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
        DeleteEntireLibraryCommand.NotifyCanExecuteChanged();
        StatusMessage = _allMediaCards.Count == 0
            ? "No media in library. Use Find/Add to add shows or movies."
            : MediaCards.Count == 0
                ? "No media matches the current search/filter."
                : $"Loaded {MediaCards.Count} media item(s).";
    }

    [RelayCommand]
    private void OpenImportPanel()
    {
        IsImportPanelOpen = true;
        ImportStatusMessage = ImportGroups.Count == 0
            ? "Add one or more folders to scan existing hardlinked media."
            : ImportStatusMessage;
    }

    [RelayCommand]
    private void CloseImportPanel()
    {
        IsImportPanelOpen = false;
    }

    [RelayCommand]
    private void AddImportFolder()
    {
        var selected = BrowseFolder("Add existing media folder");
        if (string.IsNullOrWhiteSpace(selected) ||
            ImportFolders.Any(folder => string.Equals(folder, selected, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ImportFolders.Add(selected);
        SelectedImportFolder = selected;
        NotifyImportStateChanged();
    }

    [RelayCommand]
    private void RemoveSelectedImportFolder()
    {
        if (string.IsNullOrWhiteSpace(SelectedImportFolder))
        {
            return;
        }

        ImportFolders.Remove(SelectedImportFolder);
        SelectedImportFolder = ImportFolders.FirstOrDefault();
        NotifyImportStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanScanImportFolders))]
    private async Task ScanImportFolders()
    {
        await RunImportActionAsync(async () =>
        {
            ImportStatusMessage = "Scanning and matching existing media...";
            var result = await _mediaImportService.PreviewAsync(ImportFolders);
            ImportGroups.Clear();
            foreach (var group in result.Groups)
            {
                ImportGroups.Add(new MediaImportGroupViewModel(group));
            }

            RefreshImportGroupTabs();
            ImportStatusMessage = result.Summary;
        });
    }

    [RelayCommand]
    private async Task SearchImportGroup(MediaImportGroupViewModel? group)
    {
        if (group is null || group.MediaKind is not (MediaKind.Movie or MediaKind.TvEpisode))
        {
            return;
        }

        await RunImportActionAsync(async () =>
        {
            ImportStatusMessage = $"Searching TMDB for '{group.ManualSearchQuery}'...";
            var candidates = await _mediaImportService.SearchCandidatesAsync(group.MediaKind, group.ManualSearchQuery);
            group.ReplaceCandidates(candidates);
            ImportStatusMessage = candidates.Count == 0
                ? "No TMDB candidates found."
                : $"Loaded {candidates.Count} candidate(s) for {group.ParsedTitle}.";
        });
    }

    [RelayCommand]
    private void ApplyImportCandidate(MediaImportCandidateViewModel? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        candidate.Group.ApplyCandidate(candidate.Candidate);
        RefreshImportGroupTabs();
        NotifyImportStateChanged();
        ImportStatusMessage = $"Selected {candidate.DisplayTitle} for import.";
    }

    [RelayCommand(CanExecute = nameof(CanImportSelected))]
    private async Task ImportSelected()
    {
        await RunImportActionAsync(async () =>
        {
            var groups = ImportGroups
                .Where(group => group.CanImport && group.SelectedCandidate is not null)
                .Select(group => new MediaImportCommitGroup
                {
                    MediaKind = group.MediaKind,
                    SelectedCandidate = group.SelectedCandidate!,
                    Items = group.Files
                        .Where(file => file.IsIncluded)
                        .Select(file => file.SourceItem)
                        .ToList()
                })
                .ToList();

            if (groups.Count == 0)
            {
                ImportStatusMessage = "No ready groups are selected for import.";
                return;
            }

            ImportStatusMessage = "Importing selected media...";
            var result = await _mediaImportService.CommitAsync(groups);
            RefreshLibrary();
            SelectImportedMedia(result);
            IsImportPanelOpen = false;
            ImportStatusMessage = result.Summary;
            StatusMessage = result.Summary;
        });
    }

    [RelayCommand]
    private void SearchMedia()
    {
        MediaSearchText = (MediaSearchText ?? string.Empty).Trim();
        MediaSearchQuery = MediaSearchText;
    }

    [RelayCommand(CanExecute = nameof(CanClearMediaSearch))]
    private void ClearMediaSearch()
    {
        MediaSearchText = string.Empty;
        MediaSearchQuery = string.Empty;
    }

    private bool CanClearMediaSearch() => HasAppliedMediaSearch;

    [RelayCommand]
    private void ToggleMediaSortDirection()
    {
        IsSortAscending = !IsSortAscending;
    }

    [RelayCommand(CanExecute = nameof(CanClearWatchStatusFilter))]
    private void ClearWatchStatusFilter()
    {
        if (!CanClearWatchStatusFilter())
        {
            return;
        }

        _suppressWatchStatusFilterApply = true;
        try
        {
            WatchStatusFilterOption.ClearAll(WatchStatusFilterOptions);
        }
        finally
        {
            _suppressWatchStatusFilterApply = false;
        }

        NotifyWatchStatusFilterPresentationChanged();
        ApplyMediaCardFilterAndSort();
        PersistLibraryUiState();
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

    [RelayCommand(CanExecute = nameof(CanAddMovieToCart))]
    private void AddMovieToCart(LibraryMovieDetailViewModel? movie)
    {
        if (movie is null)
        {
            return;
        }

        try
        {
            _torrentCartService.AddMovieOrder(movie.Id, movie.Title);
            StatusMessage = $"Added movie '{movie.Title}' to cart.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddEpisodeToCart))]
    private void AddEpisodeToCart(LibraryEpisodeRowViewModel? episode)
    {
        if (episode is null)
        {
            return;
        }

        try
        {
            _torrentCartService.AddEpisodeOrder(
                episode.ShowId,
                episode.Id,
                episode.SeasonNumber,
                episode.EpisodeNumber,
                episode.Title);
            StatusMessage = $"Added {episode.EpisodeCode} to cart.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddSeasonPackToCart))]
    private void AddSeasonPackToCart(LibrarySeasonViewModel? season)
    {
        if (season is null)
        {
            return;
        }

        try
        {
            _torrentCartService.AddSeasonPackOrder(season.ShowId, season.SeasonNumber);
            StatusMessage = $"Added season {season.SeasonNumber:00} pack to cart.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void AddAllMissingEpisodesToCart(LibrarySeasonViewModel? season)
    {
        if (season is null || season.IsPackMode)
        {
            return;
        }

        var addedCount = 0;
        foreach (var episode in season.Episodes.Where(episode => episode.CanAddToCart))
        {
            try
            {
                _torrentCartService.AddEpisodeOrder(
                    episode.ShowId,
                    episode.Id,
                    episode.SeasonNumber,
                    episode.EpisodeNumber,
                    episode.Title);
                addedCount++;
            }
            catch (InvalidOperationException)
            {
            }
        }

        StatusMessage = addedCount == 0
            ? $"No missing episodes in season {season.SeasonNumber:00} to add."
            : $"Added {addedCount} episode(s) from S{season.SeasonNumber:00} to cart.";
    }

    [RelayCommand(CanExecute = nameof(CanLinkEpisode))]
    private async Task LinkEpisode(LibraryEpisodeRowViewModel? episode)
    {
        if (episode is null)
        {
            return;
        }

        try
        {
            if (episode.IsOrphan && episode.SourceItemId is long sourceItemId)
            {
                StatusMessage = $"Removing orphan library entry for {episode.Title}...";
                var orphanResult = _autoTorrentLinkService.RemoveOrphanPackSpecialLink(sourceItemId);
                await ReloadSelectedDetailAsync();
                StatusMessage = $"Removed orphan entry: {orphanResult.Summary}.";
                return;
            }

            if (episode.IsOrphanSeparator || !episode.IsTrackedEpisode)
            {
                return;
            }

            if (episode.IsLinked)
            {
                StatusMessage = $"Removing library link for {episode.EpisodeCode}...";
                var unlinkResult = _autoTorrentLinkService.RemoveEpisodeLinks(
                    episode.ShowId,
                    episode.SeasonNumber,
                    episode.EpisodeNumber);
                _trackedShowService.RefreshAvailability(episode.ShowId);
                await ReloadSelectedDetailAsync();
                StatusMessage = $"Removed library link for {episode.EpisodeCode}: {unlinkResult.Summary}.";
                return;
            }

            StatusMessage = $"Creating library link for {episode.EpisodeCode}...";
            var result = await _autoTorrentLinkService.LinkEpisodeAsync(
                episode.ShowId,
                episode.SeasonNumber,
                episode.EpisodeNumber);
            _trackedShowService.RefreshAvailability(episode.ShowId);
            await ReloadSelectedDetailAsync();
            StatusMessage = $"Library link for {episode.EpisodeCode}: {result.Summary}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Link failed for {episode.EpisodeCode}: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanResetEpisode))]
    private async Task ResetEpisode(LibraryEpisodeRowViewModel? episode)
    {
        if (episode is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Reset download/link state for {episode.EpisodeCode}?\n\nThis clears all download and link data so the episode shows as Missing and can be searched again. Files still on disk are not deleted.",
            "Reset Episode",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            StatusMessage = $"Resetting {episode.EpisodeCode}...";
            _autoTorrentLinkService.ResetEpisodeForRedownload(episode.ShowId, episode.SeasonNumber, episode.EpisodeNumber);
            _trackedShowService.RefreshAvailability(episode.ShowId);
            await ReloadSelectedDetailAsync();
            StatusMessage = $"{episode.EpisodeCode} reset — episode is now Missing and ready to search.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reset failed for {episode.EpisodeCode}: {ex.Message}";
        }
    }

    private bool CanResetEpisode(LibraryEpisodeRowViewModel? episode) => episode?.CanReset == true;

    [RelayCommand(CanExecute = nameof(CanRuleLinkSeasonPack))]
    private Task RuleLinkSeasonPack(LibrarySeasonViewModel? season) =>
        RunPackLinkAsync(season, useGeminiForSpecials: false);

    [RelayCommand(CanExecute = nameof(CanAiLinkSeasonPack))]
    private Task AiLinkSeasonPack(LibrarySeasonViewModel? season) =>
        RunPackLinkAsync(season, useGeminiForSpecials: true);

    [RelayCommand(CanExecute = nameof(CanUnlinkSeasonPack))]
    private async Task UnlinkSeasonPack(LibrarySeasonViewModel? season)
    {
        if (season is null)
        {
            return;
        }

        try
        {
            StatusMessage = $"Removing library links for season {season.SeasonNumber:00} pack...";
            var unlinkResult = _autoTorrentLinkService.RemoveSeasonPackLinks(season.ShowId, season.SeasonNumber);
            _trackedShowService.RefreshAvailability(season.ShowId);
            await ReloadSelectedDetailAsync();
            StatusMessage = $"Removed pack library links for S{season.SeasonNumber:00}: {unlinkResult.Summary}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pack unlink failed for S{season.SeasonNumber:00}: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanCleanupSeasonPack))]
    private async Task CleanupSeasonPack(LibrarySeasonViewModel? season)
    {
        if (season is null)
        {
            return;
        }

        try
        {
            StatusMessage = $"Cleaning up season {season.SeasonNumber:00} pack...";
            var result = _autoTorrentLinkService.ResetSeasonPackForRedownload(season.ShowId, season.SeasonNumber);
            RefreshLibrary();
            await ReloadSelectedDetailAsync();
            StatusMessage = $"Pack cleanup for S{season.SeasonNumber:00}: {result.Summary}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pack cleanup failed for S{season.SeasonNumber:00}: {ex.Message}";
        }
    }

    private async Task RunPackLinkAsync(LibrarySeasonViewModel? season, bool useGeminiForSpecials)
    {
        if (season is null)
        {
            return;
        }

        try
        {
            StatusMessage = useGeminiForSpecials
                ? $"Creating AI-assisted library links for season {season.SeasonNumber:00} pack..."
                : $"Creating library links for season {season.SeasonNumber:00} pack (rules only)...";

            if (useGeminiForSpecials && !_geminiLinkConfirmationService.TryConfirmPackLink())
            {
                StatusMessage = "Pack linking cancelled.";
                return;
            }

            var bypassCache = false;
            var isRetry = false;
            SeasonPackLinkPreview? preview = null;
            AutoTorrentLinkResult? result = null;

            PackLinkProgressWindow? progressWindow = null;
            PackLinkProgressViewModel? progressViewModel = null;
            IProgress<PackLinkProgressUpdate>? progress = null;

            if (useGeminiForSpecials)
            {
                var show = _databaseService.GetTrackedShow(season.ShowId);
                var showTitle = show?.DisplayTitle ?? "Show";
                progressViewModel = new PackLinkProgressViewModel(showTitle, season.SeasonNumber);
                progressWindow = new PackLinkProgressWindow(progressViewModel)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                progress = new Progress<PackLinkProgressUpdate>(progressViewModel.Report);
                progressWindow.Show();
            }

            try
            {
                while (true)
                {
                    progressViewModel?.ResetForRetry(isRetry);
                    if (progressWindow is not null)
                    {
                        progressWindow.Show();
                        progressWindow.Activate();
                    }

                    try
                    {
                        preview = await _autoTorrentLinkService.PrepareSeasonPackLinkAsync(
                            season.ShowId,
                            season.SeasonNumber,
                            progress,
                            useGeminiForSpecials,
                            bypassCache);

                        if (preview.RequiresReview)
                        {
                            progressViewModel?.PrepareForReview("AI mapping complete. Opening review...");
                        }
                        else if (progressViewModel is not null)
                        {
                            progressViewModel.MarkComplete("Pack analysis complete.");
                        }
                    }
                    catch (Exception ex)
                    {
                        progressViewModel?.MarkFailed(ex.Message, ex.ToString());
                        StatusMessage = $"Pack link failed for S{season.SeasonNumber:00}: {ex.Message}";
                        return;
                    }

                    if (!preview.RequiresReview)
                    {
                        result = await _autoTorrentLinkService.ApplySeasonPackLinkAsync(preview);
                        break;
                    }

                    progressWindow?.Hide();

                    var reviewViewModel = new PackLinkReviewViewModel();
                    reviewViewModel.LoadPreview(preview);
                    var reviewWindow = new PackLinkReviewWindow(reviewViewModel)
                    {
                        Owner = System.Windows.Application.Current.MainWindow
                    };

                    bool? reviewAccepted;
                    try
                    {
                        reviewAccepted = reviewWindow.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Pack review window failed for S{season.SeasonNumber:00}: {ex.Message}";
                        if (progressViewModel is not null && useGeminiForSpecials)
                        {
                            progressViewModel.MarkFailed(ex.Message, ex.ToString());
                            progressWindow = new PackLinkProgressWindow(progressViewModel)
                            {
                                Owner = System.Windows.Application.Current.MainWindow
                            };
                            progressWindow.Show();
                        }

                        return;
                    }

                    if (reviewAccepted != true)
                    {
                        StatusMessage = "Pack linking cancelled.";
                        progressViewModel?.MarkCancelled("Pack linking cancelled.");
                        progressWindow?.Show();
                        progressWindow?.Activate();
                        return;
                    }

                    switch (reviewViewModel.Decision)
                    {
                        case PackLinkReviewDecision.Retry:
                            bypassCache = true;
                            isRetry = true;
                            continue;
                        case PackLinkReviewDecision.Accept:
                            result = await _autoTorrentLinkService.ApplySeasonPackLinkAsync(preview);
                            break;
                        default:
                            StatusMessage = "Pack linking cancelled.";
                            progressViewModel?.MarkCancelled("Pack linking cancelled.");
                            progressWindow?.Show();
                            progressWindow?.Activate();
                            return;
                    }

                    break;
                }

                if (result is not null)
                {
                    _trackedShowService.RefreshAvailability(season.ShowId);
                    await ReloadSelectedDetailAsync();
                    StatusMessage = $"Pack library links for S{season.SeasonNumber:00}: {result.Summary}.";
                }
            }
            finally
            {
                if (progressWindow is not null)
                {
                    if (progressViewModel?.IsFailed == true)
                    {
                        progressViewModel.AllowClose();
                    }
                    else if (result is not null && progressViewModel?.IsCancelled != true)
                    {
                        progressViewModel?.AllowClose();
                        progressWindow.Close();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pack link failed for S{season.SeasonNumber:00}: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanLinkMovie))]
    private async Task LinkMovie(LibraryMovieDetailViewModel? movie)
    {
        if (movie is null)
        {
            return;
        }

        try
        {
            if (movie.IsLinked)
            {
                StatusMessage = $"Removing library link for {movie.Title}...";
                var unlinkResult = _autoTorrentLinkService.RemoveMovieLinks(movie.Id);
                _trackedMovieService.RefreshAvailability(movie.Id);
                await ReloadSelectedDetailAsync();
                StatusMessage = $"Removed library link for {movie.Title}: {unlinkResult.Summary}.";
                return;
            }

            StatusMessage = $"Creating library link for {movie.Title}...";
            var result = await _autoTorrentLinkService.LinkMovieAsync(movie.Id);
            _trackedMovieService.RefreshAvailability(movie.Id);
            await ReloadSelectedDetailAsync();
            StatusMessage = $"Library link for {movie.Title}: {result.Summary}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Link failed for {movie.Title}: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanResetMovie))]
    private async Task ResetMovie(LibraryMovieDetailViewModel? movie)
    {
        if (movie is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Reset download/link state for {movie.Title}?\n\nThis clears all download and link data so the movie shows as Missing and can be searched again. Files still on disk are not deleted.",
            "Reset Movie",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            StatusMessage = $"Resetting {movie.Title}...";
            _autoTorrentLinkService.ResetMovieForRedownload(movie.Id);
            _trackedMovieService.RefreshAvailability(movie.Id);
            await ReloadSelectedDetailAsync();
            StatusMessage = $"{movie.Title} reset — movie is now Missing and ready to search.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reset failed for {movie.Title}: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReconcileExistingTorrents()
    {
        try
        {
            StatusMessage = "Reconciling existing qBittorrent torrents for entire library...";
            var result = await _torrentReconciliationService.ReconcileAsync(TorrentReconciliationScope.All);
            await ReloadSelectedDetailAsync();
            StatusMessage = $"Torrent reconciliation complete. {result.Summary}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Torrent reconciliation failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshAllFromTmdb()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Refresh metadata from TMDB for all shows and movies?\n\nShow status and aired episodes will be updated from TMDB.",
            "Refresh Metadata from TMDB",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        await RunImportActionAsync(async () =>
        {
            StatusMessage = "Refreshing metadata from TMDB...";
            var result = await _mediaMetadataSyncService.RefreshAllLibraryFromTmdbAsync();
            RefreshLibrary();
            await ReloadSelectedDetailAsync();
            StatusMessage = $"TMDB refresh complete. {result.Summary}";
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMedia))]
    private void OpenSelectedTmdbPage()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var path = SelectedMediaCard.MediaKind == MediaKind.Movie ? "movie" : "tv";
        var url = $"https://www.themoviedb.org/{path}/{SelectedMediaCard.TmdbId}";
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void CopyToClipboard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        System.Windows.Clipboard.SetText(text.Trim());
        StatusMessage = $"Copied \"{text.Trim()}\" to clipboard.";
    }

    [RelayCommand]
    private void ToggleAlternativeTitleRecipeSearchExclusion(AlternativeTitleChipViewModel? chip)
    {
        if (chip is null || string.IsNullOrWhiteSpace(chip.Title))
        {
            return;
        }

        var exclude = !chip.IsExcludedFromRecipeSearch;
        if (chip.IsMovie)
        {
            _trackedMovieService.SetAlternativeTitleExcludedFromSearch(chip.MediaId, chip.Title, exclude);
        }
        else
        {
            _trackedShowService.SetAlternativeTitleExcludedFromSearch(chip.MediaId, chip.Title, exclude);
        }

        chip.IsExcludedFromRecipeSearch = exclude;
        StatusMessage = exclude
            ? $"Excluded \"{chip.Title}\" from recipe search."
            : $"Included \"{chip.Title}\" in recipe search.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMedia))]
    private async Task RefreshSelectedFromTmdb()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        await RunImportActionAsync(async () =>
        {
            StatusMessage = $"Refreshing {SelectedMediaCard.Title} from TMDB...";
            if (SelectedMediaCard.IsShow)
            {
                var result = await _mediaMetadataSyncService.RefreshShowAsync(SelectedMediaCard.Id);
                RefreshLibrary();
                await ReloadSelectedDetailAsync();
                StatusMessage = result.Success
                    ? $"TMDB refresh complete for {result.Title} via {result.OrganizationLabel}. {result.NewEpisodesAdded} new episode(s) added."
                    : $"TMDB refresh failed for {result.Title}: {result.ErrorMessage}";
                return;
            }

            var movieResult = await _mediaMetadataSyncService.RefreshMovieAsync(SelectedMediaCard.Id);
            RefreshLibrary();
            await ReloadSelectedDetailAsync();
            StatusMessage = movieResult.Success
                ? $"TMDB refresh complete for {movieResult.Title}."
                : $"TMDB refresh failed for {movieResult.Title}: {movieResult.ErrorMessage}";
        });
    }

    partial void OnSelectedShowSeriesStatusChanged(ShowSeriesStatus value)
    {
        OnPropertyChanged(nameof(SelectedShowSeriesStatusLabel));

        if (_suppressSeriesStatusUpdate || SelectedShow is null || SelectedShow.SeriesStatus == value)
        {
            return;
        }

        _trackedShowService.UpdateSeriesStatus(SelectedShow.Id, value);
        RebuildSelectedShowDetail();
        RefreshLibrary();
    }

    partial void OnSelectedWatchStatusChanged(UserWatchStatus value)
    {
        if (_suppressWatchProgressUpdate)
        {
            return;
        }

        PersistSelectedWatchProgress(value, SelectedWatchedEpisodes);
    }

    partial void OnSelectedWatchedEpisodesChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedWatchEpisodesLabel));
        OnPropertyChanged(nameof(CanIncrementWatchedEpisodes));
        OnPropertyChanged(nameof(CanDecrementWatchedEpisodes));
        IncrementWatchedEpisodesCommand.NotifyCanExecuteChanged();
        DecrementWatchedEpisodesCommand.NotifyCanExecuteChanged();

        if (_suppressWatchProgressUpdate || !IsSelectedShow)
        {
            return;
        }

        PersistSelectedWatchProgress(SelectedWatchStatus, value);
    }

    partial void OnSelectedWatchTotalEpisodesChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedWatchEpisodesLabel));
        OnPropertyChanged(nameof(CanIncrementWatchedEpisodes));
        IncrementWatchedEpisodesCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedRatingChanged(double value)
    {
        var clamped = ClampRating(value);
        if (Math.Abs(clamped - value) > 0.0001)
        {
            SelectedRating = clamped;
            return;
        }

        OnPropertyChanged(nameof(CanIncrementRating));
        OnPropertyChanged(nameof(CanDecrementRating));
        OnPropertyChanged(nameof(SelectedRatingText));
        IncrementRatingCommand.NotifyCanExecuteChanged();
        DecrementRatingCommand.NotifyCanExecuteChanged();

        if (_suppressRatingThoughtUpdate || !HasSelectedMedia)
        {
            return;
        }

        PersistSelectedRatingAndThought();
    }

    partial void OnSelectedThoughtChanged(string value)
    {
        OnPropertyChanged(nameof(HasThoughtText));
        OnPropertyChanged(nameof(ThoughtDisplayText));
        ToggleThoughtPopupCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEditingThoughtChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowThoughtDisplay));
        OnPropertyChanged(nameof(ShowThoughtEditor));
        if (value)
        {
            IsThoughtPopupOpen = false;
        }
    }

    partial void OnMediaSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasMediaSearchText));
    }

    partial void OnMediaSearchQueryChanged(string value)
    {
        ApplyMediaCardFilterAndSort();
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(HasNoFilterMatches));
        OnPropertyChanged(nameof(HasAppliedMediaSearch));
        ClearMediaSearchCommand.NotifyCanExecuteChanged();
        PersistLibraryUiState();
    }

    [RelayCommand(CanExecute = nameof(IsSelectedShow))]
    private void CycleSelectedShowSeriesStatus()
    {
        SelectedShowSeriesStatus = SelectedShowSeriesStatus switch
        {
            ShowSeriesStatus.Unknown => ShowSeriesStatus.Ongoing,
            ShowSeriesStatus.Ongoing => ShowSeriesStatus.Finished,
            _ => ShowSeriesStatus.Unknown
        };
    }

    [RelayCommand(CanExecute = nameof(CanIncrementWatchedEpisodes))]
    private void IncrementWatchedEpisodes()
    {
        if (!CanIncrementWatchedEpisodes)
        {
            return;
        }

        PersistSelectedWatchProgress(SelectedWatchStatus, SelectedWatchedEpisodes + 1);
    }

    [RelayCommand(CanExecute = nameof(CanDecrementWatchedEpisodes))]
    private void DecrementWatchedEpisodes()
    {
        if (!CanDecrementWatchedEpisodes)
        {
            return;
        }

        PersistSelectedWatchProgress(SelectedWatchStatus, SelectedWatchedEpisodes - 1);
    }

    [RelayCommand(CanExecute = nameof(CanIncrementRating))]
    private void IncrementRating()
    {
        if (!CanIncrementRating)
        {
            return;
        }

        SelectedRating = ClampRating(SelectedRating + RatingStep);
    }

    [RelayCommand(CanExecute = nameof(CanDecrementRating))]
    private void DecrementRating()
    {
        if (!CanDecrementRating)
        {
            return;
        }

        SelectedRating = ClampRating(SelectedRating - RatingStep);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMedia))]
    private void BeginEditThought()
    {
        if (!HasSelectedMedia)
        {
            return;
        }

        IsThoughtPopupOpen = false;
        IsEditingThought = true;
    }

    [RelayCommand]
    private void CommitThought()
    {
        if (!IsEditingThought)
        {
            return;
        }

        SelectedThought = NormalizeThought(SelectedThought);
        IsEditingThought = false;
        PersistSelectedRatingAndThought();
    }

    [RelayCommand(CanExecute = nameof(HasThoughtText))]
    private void ToggleThoughtPopup()
    {
        if (!HasThoughtText)
        {
            IsThoughtPopupOpen = false;
            return;
        }

        IsThoughtPopupOpen = !IsThoughtPopupOpen;
    }

    [RelayCommand(CanExecute = nameof(IsSelectedShow))]
    private void SetAutoTrack()
    {
        if (SelectedShow is null)
        {
            return;
        }

        var show = _trackedShowService.GetShows().FirstOrDefault(item => item.Id == SelectedShow.Id);
        if (show is null)
        {
            StatusMessage = "Selected show was not found.";
            return;
        }

        var episodes = _trackedShowService.GetEpisodes(show.Id);
        var seasons = _trackedShowService.GetSeasons(show.Id);

        var folderOptions = _downloadFolderCatalogService.GetKnownDownloadFolders();
        var defaultFolder = _settingsService.Current.AutoTorrent.DownloadFolders.FirstOrDefault()
            ?? _settingsService.Current.AutoTorrent.DownloadFolder;

        var dialog = new SetAutoTrackDialog(
            episodes,
            folderOptions,
            defaultFolder,
            show.AutoTrackFromSeason,
            show.AutoTrackFromEpisode,
            show.AutoTrackDownloadFolder,
            show.IsAutoTracked ? show.AutoTrackAutoReconcileAndLink : true,
            seasons)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _trackedShowService.SetAutoTrackCheckpoint(
            show.Id,
            dialog.SelectedSeason,
            dialog.SelectedEpisode,
            dialog.SelectedDownloadFolder,
            dialog.AutoReconcileAndLink);
        RebuildSelectedShowDetail();
        OnPropertyChanged(nameof(ShowStopAutoTrackButton));
        StatusMessage = $"Auto-track enabled for {show.DisplayTitle} from S{dialog.SelectedSeason:00}E{dialog.SelectedEpisode:00}.";
    }

    [RelayCommand(CanExecute = nameof(CanStopAutoTrack))]
    private void StopAutoTrack()
    {
        if (SelectedShow is null || !SelectedShow.IsAutoTracked)
        {
            return;
        }

        _trackedShowService.StopAutoTrack(SelectedShow.Id);
        RebuildSelectedShowDetail();
        OnPropertyChanged(nameof(ShowStopAutoTrackButton));
        StatusMessage = $"Stopped auto-tracking {SelectedShow.Title}.";
    }

    private bool CanStopAutoTrack() => SelectedShow?.IsAutoTracked == true;

    [RelayCommand(CanExecute = nameof(IsSelectedShow))]
    private async Task ChangeEpisodeOrganizationAsync()
    {
        if (SelectedShow is null)
        {
            return;
        }

        await RunImportActionAsync(async () =>
        {
            var show = _trackedShowService.GetShows().FirstOrDefault(item => item.Id == SelectedShow.Id);
            if (show is null)
            {
                StatusMessage = "Selected show was not found.";
                return;
            }

            StatusMessage = $"Loading episode groups for {show.DisplayTitle}...";
            var episodeGroups = await _trackedShowService.GetEpisodeGroupsAsync(show.TmdbId);
            if (episodeGroups.Count == 0 && !show.UsesEpisodeGroup)
            {
                System.Windows.MessageBox.Show(
                    "This show has no TMDB episode groups. Only default season organization is available.",
                    "Episode Organization",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                StatusMessage = "No episode groups available.";
                return;
            }

            var summary = await _trackedShowService.GetShowSummaryAsync(show.TmdbId);
            var dialog = new EpisodeOrganizationDialog(
                show.DisplayTitle,
                summary.SeasonCount,
                summary.EpisodeCount,
                episodeGroups,
                show.EpisodeGroupId,
                confirmButtonText: "Apply")
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (dialog.ShowDialog() != true)
            {
                StatusMessage = "Organization change cancelled.";
                return;
            }

            var selectedGroupId = dialog.SelectedEpisodeGroupId;
            var confirm = System.Windows.MessageBox.Show(
                $"This rebuilds season/episode structure for {show.DisplayTitle}.\n\n" +
                "Hardlinks, torrent candidates, and pack links will be cleared.\n" +
                "Watch status and watched-episode count are kept, but may no longer match the new numbering.\n\n" +
                "Continue?",
                "Change Episode Organization",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (confirm != System.Windows.MessageBoxResult.Yes)
            {
                StatusMessage = "Organization change cancelled.";
                return;
            }

            StatusMessage = $"Rebuilding {show.DisplayTitle} with new organization...";
            var rebuilt = await _trackedShowService.SwitchEpisodeOrganizationAsync(
                show,
                selectedGroupId,
                dialog.SelectedEpisodeGroupName);
            _torrentCartService.ClearCart(MediaKind.TvEpisode, show.Id);
            RefreshLibrary();
            await ReloadSelectedDetailAsync();
            StatusMessage =
                $"Organization set for {rebuilt.DisplayTitle} → {rebuilt.EpisodeOrganizationLabel}. {rebuilt.TotalEpisodes} episode(s). Sync TMDB will keep this route.";
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMedia))]
    private void DeleteSelectedMedia()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Delete '{SelectedMediaCard.Title}' from library?\n\nThis removes hardlinks, seasons/episodes, your rating/review, and clears its cart.",
            "Delete Media",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var result = _libraryManagementService.DeleteSelectedMedia(SelectedMediaCard.MediaKind, SelectedMediaCard.Id);
        SelectedMediaCard = null;
        RefreshLibrary();
        StatusMessage = result.HardlinkErrorCount > 0
            ? $"{result.Summary} {result.HardlinkErrorCount} hardlink error(s)."
            : result.Summary;
    }

    [RelayCommand(CanExecute = nameof(HasLibraryMedia))]
    private void DeleteEntireLibrary()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Delete the entire library?\n\nThis removes all hardlinks, tracked shows/movies, seasons, episodes, fetch jobs, and clears all carts.",
            "Delete Entire Library",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var result = _libraryManagementService.DeleteEntireLibrary();
        SelectedMediaCard = null;
        SelectedShow = null;
        SelectedMovie = null;
        RefreshLibrary();
        StatusMessage = result.HardlinkErrorCount > 0
            ? $"{result.Summary} {result.HardlinkErrorCount} hardlink error(s)."
            : result.Summary;
    }

    partial void OnSelectedMediaCardChanged(LibraryMediaCardViewModel? value)
    {
        foreach (var card in MediaCards)
        {
            card.IsSelected = ReferenceEquals(card, value);
        }

        var isSameMedia = value?.Id == _loadedDetailMediaId && value?.MediaKind == _loadedDetailMediaKind;
        if (!isSameMedia)
        {
            ShowHiddenSeasons = false;
            _loadedDetailMediaId = value?.Id;
            _loadedDetailMediaKind = value?.MediaKind;
            _ = LoadSelectedMediaAsync(value);
        }

        PersistLibraryUiState();
        OnPropertyChanged(nameof(HasSelectedMedia));
        OnPropertyChanged(nameof(IsSelectedShow));
        OnPropertyChanged(nameof(IsSelectedMovie));
        OnPropertyChanged(nameof(ShowStopAutoTrackButton));
        OnPropertyChanged(nameof(CanIncrementRating));
        OnPropertyChanged(nameof(CanDecrementRating));
        OnPropertyChanged(nameof(ShowThoughtDisplay));
        OnPropertyChanged(nameof(ShowThoughtEditor));
        ChangeEpisodeOrganizationCommand.NotifyCanExecuteChanged();
        SetAutoTrackCommand.NotifyCanExecuteChanged();
        StopAutoTrackCommand.NotifyCanExecuteChanged();
        IncrementRatingCommand.NotifyCanExecuteChanged();
        DecrementRatingCommand.NotifyCanExecuteChanged();
        BeginEditThoughtCommand.NotifyCanExecuteChanged();
        ToggleThoughtPopupCommand.NotifyCanExecuteChanged();
    }

    partial void OnMediaSortFieldChanged(MediaCardSortField value)
    {
        if (!_isRestoringLibraryUiState)
        {
            IsSortAscending = MediaCardSort.DefaultIsAscending(value);
        }

        ApplyMediaCardFilterAndSort();
        OnPropertyChanged(nameof(MediaSortDirectionToolTip));
        PersistLibraryUiState();
    }

    partial void OnIsSortAscendingChanged(bool value)
    {
        ApplyMediaCardFilterAndSort();
        OnPropertyChanged(nameof(MediaSortDirectionToolTip));
        PersistLibraryUiState();
    }

    private void SubscribeWatchStatusFilterOptions()
    {
        foreach (var option in WatchStatusFilterOptions)
        {
            option.PropertyChanged += OnWatchStatusFilterOptionChanged;
        }
    }

    private void OnWatchStatusFilterOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WatchStatusFilterOption.IsSelected))
        {
            return;
        }

        NotifyWatchStatusFilterPresentationChanged();
        if (_isRestoringLibraryUiState || _suppressWatchStatusFilterApply)
        {
            return;
        }

        ApplyMediaCardFilterAndSort();
        PersistLibraryUiState();
    }

    private void NotifyWatchStatusFilterPresentationChanged()
    {
        OnPropertyChanged(nameof(WatchStatusFilterLabel));
        OnPropertyChanged(nameof(WatchStatusFilterLabelStatus));
        OnPropertyChanged(nameof(HasSelectedWatchStatusFilter));
        ClearWatchStatusFilterCommand.NotifyCanExecuteChanged();
    }

    private void RestoreLibraryUiState()
    {
        _isRestoringLibraryUiState = true;
        try
        {
            var ui = _settingsService.Current.Ui ?? new UiSettings();
            MediaCardSort.Restore(
                ui.LibraryMediaSortField,
                ui.LibraryMediaSortAscending,
                ui.LibraryMediaSortMode,
                out var field,
                out var ascending);
            MediaSortField = field;
            IsSortAscending = ascending;
            MediaSearchQuery = ui.LibraryMediaSearchQuery ?? string.Empty;
            MediaSearchText = MediaSearchQuery;
            WatchStatusFilterOption.ApplySaved(
                WatchStatusFilterOptions,
                ui.LibraryWatchStatusFilters,
                ui.LibraryWatchStatusFilter);
            NotifyWatchStatusFilterPresentationChanged();
            _pendingRestoreMediaId = ui.LibrarySelectedMediaId;
            _pendingRestoreMediaKind = ui.LibrarySelectedMediaKind;
        }
        finally
        {
            _isRestoringLibraryUiState = false;
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

    private void PersistLibraryUiState()
    {
        if (_isRestoringLibraryUiState)
        {
            return;
        }

        var ui = _settingsService.Current.Ui ??= new UiSettings();
        var search = MediaSearchQuery ?? string.Empty;
        var filters = WatchStatusFilterOption.GetSelectedStatuses(WatchStatusFilterOptions);
        var selectedId = SelectedMediaCard?.Id;
        var selectedKind = SelectedMediaCard?.MediaKind;
        if (ui.LibraryMediaSortField == MediaSortField &&
            ui.LibraryMediaSortAscending == IsSortAscending &&
            string.Equals(ui.LibraryMediaSearchQuery, search, StringComparison.Ordinal) &&
            ui.LibraryWatchStatusFilters is not null &&
            ui.LibraryWatchStatusFilters.SequenceEqual(filters) &&
            ui.LibrarySelectedMediaId == selectedId &&
            ui.LibrarySelectedMediaKind == selectedKind)
        {
            return;
        }

        ui.LibraryMediaSortField = MediaSortField;
        ui.LibraryMediaSortAscending = IsSortAscending;
        ui.LibraryMediaSearchQuery = search;
        ui.LibraryWatchStatusFilters = filters;
        ui.LibraryWatchStatusFilter = filters.Count == 1 ? filters[0] : null;
        ui.LibrarySelectedMediaId = selectedId;
        ui.LibrarySelectedMediaKind = selectedKind;
        _settingsService.Save();
    }

    partial void OnIsImportBusyChanged(bool value)
    {
        NotifyImportStateChanged();
    }

    partial void OnIsImportPanelOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(HasSelectedMedia));
    }

    partial void OnShowHiddenSeasonsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHiddenSeasonsButtonLabel));
    }

    partial void OnSelectedShowChanged(LibraryShowDetailViewModel? value)
    {
        OnPropertyChanged(nameof(ShowHiddenSeasonsButtonLabel));
        OnPropertyChanged(nameof(ShowStopAutoTrackButton));
        OnPropertyChanged(nameof(ShowWatchEpisodeControls));
        OnPropertyChanged(nameof(CanIncrementWatchedEpisodes));
        OnPropertyChanged(nameof(CanDecrementWatchedEpisodes));
        IncrementWatchedEpisodesCommand.NotifyCanExecuteChanged();
        DecrementWatchedEpisodesCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedMovieChanged(LibraryMovieDetailViewModel? value)
    {
        OnPropertyChanged(nameof(ShowStopAutoTrackButton));
    }

    [RelayCommand]
    private void ToggleSeasonHidden(LibrarySeasonViewModel? season)
    {
        if (season is null || SelectedShow is null)
        {
            return;
        }

        var wasHidden = season.IsHidden;
        _trackedShowService.UpdateSeasonHidden(season.ShowId, season.SeasonNumber, !wasHidden);
        RebuildSelectedShowDetail();
        StatusMessage = wasHidden
            ? $"Season {season.SeasonNumber:00} is visible again."
            : $"Season {season.SeasonNumber:00} hidden.";
    }

    [RelayCommand]
    private void ToggleShowHiddenSeasons()
    {
        ShowHiddenSeasons = !ShowHiddenSeasons;
        RebuildSelectedShowDetail();
    }

    private async Task LoadSelectedMediaAsync(LibraryMediaCardViewModel? card)
    {
        // Block watch-status persistence until SetWatchProgressUi finishes settling bindings.
        _suppressWatchProgressUpdate = true;

        var expandedSeasons = SelectedShow?.Seasons
            .Where(season => season.IsExpanded)
            .Select(season => season.SeasonNumber)
            .ToHashSet() ?? [];

        SelectedShow = null;
        SelectedMovie = null;
        SelectedPosterImage = null;

        if (card is null)
        {
            _loadedDetailMediaId = null;
            _loadedDetailMediaKind = null;
            SetWatchProgressUi(UserWatchStatus.None, watchedEpisodes: 0, totalEpisodes: 0);
            SetRatingThoughtUi(rating: null, thought: null);
            return;
        }

        var sourceItems = _databaseService.GetSourceItems();

        if (card.IsShow)
        {
            var show = _trackedShowService.GetShows().FirstOrDefault(item => item.Id == card.Id);
            if (show is null)
            {
                StatusMessage = "Selected show was not found.";
                return;
            }

            SelectedShow = BuildShowDetail(show, expandedSeasons, sourceItems);
            _suppressSeriesStatusUpdate = true;
            SelectedShowSeriesStatus = show.SeriesStatus;
            _suppressSeriesStatusUpdate = false;
            SetWatchProgressUi(show.WatchStatus, show.WatchedEpisodes, show.WatchEpisodeTotal);
            SetRatingThoughtUi(show.Rating, show.Thought);
            SelectedPosterImage = await _posterImageService.LoadAsync(
                card.PosterPath,
                card.MediaKind,
                card.TmdbId);
            StatusMessage = $"Viewing show: {show.DisplayTitle}";
            return;
        }

        var movie = _trackedMovieService.GetMovies().FirstOrDefault(item => item.Id == card.Id);
        if (movie is null)
        {
            StatusMessage = "Selected movie was not found.";
            return;
        }

        SelectedMovie = BuildMovieDetail(movie, sourceItems);
        SetWatchProgressUi(movie.WatchStatus, watchedEpisodes: 0, totalEpisodes: 0);
        SetRatingThoughtUi(movie.Rating, movie.Thought);
        SelectedPosterImage = await _posterImageService.LoadAsync(
            card.PosterPath,
            card.MediaKind,
            card.TmdbId);
        StatusMessage = $"Viewing movie: {movie.DisplayTitle}";
    }

    private void RefreshCartStateOnSelectedDetail()
    {
        if (SelectedShow is not null)
        {
            foreach (var season in SelectedShow.Seasons)
            {
                season.IsPackInCart = _torrentCartService.TryGetActiveSeasonPackOrder(
                    season.ShowId,
                    season.SeasonNumber,
                    out _);
                foreach (var episode in season.Episodes)
                {
                    episode.IsInCart = _torrentCartService.TryGetActiveEpisodeOrder(episode.Id, out _);
                }
            }
        }

        if (SelectedMovie is not null)
        {
            SelectedMovie.IsInCart = _torrentCartService.TryGetActiveMovieOrder(SelectedMovie.Id, out _);
        }

        AddMovieToCartCommand.NotifyCanExecuteChanged();
        AddEpisodeToCartCommand.NotifyCanExecuteChanged();
        AddSeasonPackToCartCommand.NotifyCanExecuteChanged();
        LinkMovieCommand.NotifyCanExecuteChanged();
        ResetMovieCommand.NotifyCanExecuteChanged();
        LinkEpisodeCommand.NotifyCanExecuteChanged();
        ResetEpisodeCommand.NotifyCanExecuteChanged();
        RuleLinkSeasonPackCommand.NotifyCanExecuteChanged();
        AiLinkSeasonPackCommand.NotifyCanExecuteChanged();
        UnlinkSeasonPackCommand.NotifyCanExecuteChanged();
        CleanupSeasonPackCommand.NotifyCanExecuteChanged();
    }

    private void RunRefreshSelectedDetailAfterReconcileOnUiThread()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, RunRefreshSelectedDetailAfterReconcileOnUiThread);
            return;
        }

        RefreshSelectedDetailAfterReconcile();
    }

    private void RefreshSelectedDetailAfterReconcile()
    {
        var card = SelectedMediaCard;
        if (card is null)
        {
            return;
        }

        var id = card.Id;
        var kind = card.MediaKind;

        if (card.IsShow)
        {
            RebuildSelectedShowDetail();
        }
        else
        {
            RebuildSelectedMovieDetail();
        }

        if (SelectedMediaCard?.Id != id || SelectedMediaCard?.MediaKind != kind)
        {
            return;
        }

        RefreshCartStateOnSelectedDetail();
    }

    private async Task ReloadSelectedDetailAsync()
    {
        await LoadSelectedMediaAsync(SelectedMediaCard);
        AddMovieToCartCommand.NotifyCanExecuteChanged();
        AddEpisodeToCartCommand.NotifyCanExecuteChanged();
        AddSeasonPackToCartCommand.NotifyCanExecuteChanged();
        LinkMovieCommand.NotifyCanExecuteChanged();
        ResetMovieCommand.NotifyCanExecuteChanged();
        LinkEpisodeCommand.NotifyCanExecuteChanged();
        ResetEpisodeCommand.NotifyCanExecuteChanged();
        RuleLinkSeasonPackCommand.NotifyCanExecuteChanged();
        AiLinkSeasonPackCommand.NotifyCanExecuteChanged();
        UnlinkSeasonPackCommand.NotifyCanExecuteChanged();
        CleanupSeasonPackCommand.NotifyCanExecuteChanged();
    }

    private LibraryShowDetailViewModel BuildShowDetail(
        TrackedShow show,
        IReadOnlySet<int>? expandedSeasons,
        IReadOnlyList<SourceItem> sourceItems)
    {
        var linkedEpisodeStatuses = GetLinkedEpisodeStatuses(show.TmdbId, sourceItems);
        var linkedPackOwnerSeasons = GetLinkedPackOwnerSeasons(show.TmdbId, sourceItems);
        var episodes = _trackedShowService.GetEpisodes(show.Id)
            .Select(episode => new LibraryEpisodeRowViewModel(episode)
            {
                LibraryLinkStatus = linkedEpisodeStatuses.TryGetValue(
                    (episode.SeasonNumber, episode.EpisodeNumber),
                    out var status)
                    ? status
                    : "Not linked",
                IsInCart = _torrentCartService.TryGetActiveEpisodeOrder(episode.Id, out _)
            })
            .ToList();

        var seasonRecords = _trackedShowService.GetSeasons(show.Id)
            .ToDictionary(season => season.SeasonNumber);

        var hiddenSeasonNumbers = seasonRecords.Values
            .Where(season => season.IsHidden)
            .Select(season => season.SeasonNumber)
            .ToHashSet();

        var geminiLinkAvailable = _settingsService.Current.Gemini?.Enabled == true
            && !string.IsNullOrWhiteSpace(_settingsService.Current.Gemini?.ApiKey);

        var seasons = episodes
            .GroupBy(episode => episode.SeasonNumber)
            .Where(group => ShowHiddenSeasons || !hiddenSeasonNumbers.Contains(group.Key))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                seasonRecords.TryGetValue(group.Key, out var seasonRecord);
                var seasonRows = group.ToList();
                if (group.Key == AppConstants.SpecialsSeasonNumber)
                {
                    seasonRows = AppendOrphanPackRows(show.Id, show.TmdbId, seasonRows, sourceItems);
                }

                return new LibrarySeasonViewModel(
                    show.Id,
                    group.Key,
                    seasonRows,
                    UpdateSeasonManagementMode,
                    seasonRecord)
                {
                    IsExpanded = expandedSeasons?.Contains(group.Key) == true,
                    IsPackInCart = _torrentCartService.TryGetActiveSeasonPackOrder(show.Id, group.Key, out _),
                    IsPackLinked = linkedPackOwnerSeasons.Contains(group.Key),
                    IsGeminiLinkAvailable = geminiLinkAvailable
                };
            });

        return new LibraryShowDetailViewModel(show, seasons, hiddenSeasonNumbers.Count);
    }

    private void RebuildSelectedShowDetail()
    {
        if (SelectedMediaCard?.IsShow != true)
        {
            return;
        }

        var show = _trackedShowService.GetShows().FirstOrDefault(item => item.Id == SelectedMediaCard.Id);
        if (show is null)
        {
            return;
        }

        var expandedSeasons = SelectedShow?.Seasons
            .Where(season => season.IsExpanded)
            .Select(season => season.SeasonNumber)
            .ToHashSet() ?? [];

        SelectedShow = BuildShowDetail(show, expandedSeasons, _databaseService.GetSourceItems());
        _suppressSeriesStatusUpdate = true;
        SelectedShowSeriesStatus = show.SeriesStatus;
        _suppressSeriesStatusUpdate = false;

        if (ShowHiddenSeasons && SelectedShow?.HasHiddenSeasons != true)
        {
            ShowHiddenSeasons = false;
        }

        OnPropertyChanged(nameof(ShowStopAutoTrackButton));
    }

    private LibraryMovieDetailViewModel BuildMovieDetail(TrackedMovie movie, IReadOnlyList<SourceItem> sourceItems)
    {
        return new LibraryMovieDetailViewModel(movie)
        {
            LibraryLinkStatus = IsMovieLinked(movie.TmdbId, sourceItems) ? "Linked" : "Not linked",
            IsInCart = _torrentCartService.TryGetActiveMovieOrder(movie.Id, out _)
        };
    }

    private void RebuildSelectedMovieDetail()
    {
        if (SelectedMediaCard?.IsMovie != true)
        {
            return;
        }

        var movie = _trackedMovieService.GetMovies().FirstOrDefault(item => item.Id == SelectedMediaCard.Id);
        if (movie is null)
        {
            return;
        }

        SelectedMovie = BuildMovieDetail(movie, _databaseService.GetSourceItems());
        SetWatchProgressUi(movie.WatchStatus, watchedEpisodes: 0, totalEpisodes: 0);
        SetRatingThoughtUi(movie.Rating, movie.Thought);
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

        var selectedStatuses = WatchStatusFilterOption.GetSelectedStatuses(WatchStatusFilterOptions);
        if (selectedStatuses.Count > 0)
        {
            filtered = filtered.Where(card => selectedStatuses.Contains(card.WatchStatus));
        }

        var sorted = MediaCardSort.Apply(filtered, MediaSortField, IsSortAscending);

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
            // Keep selection only when it still matches the filter. Do not auto-select the
            // next card: that reloads detail while SelectedWatchStatus may still hold the
            // status just applied, and the ComboBox TwoWay binding can cascade that status
            // onto every remaining filtered item (e.g. Watching → all marked Completed).
            SelectedMediaCard = MediaCards.FirstOrDefault(card =>
                card.Id == selectedId && card.MediaKind == selectedKind);
        }

        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(HasNoFilterMatches));
    }

    private void SetWatchProgressUi(UserWatchStatus status, int watchedEpisodes, int totalEpisodes)
    {
        var generation = ++_watchProgressSuppressGeneration;
        _suppressWatchProgressUpdate = true;
        SelectedWatchStatus = status;
        SelectedWatchTotalEpisodes = Math.Max(0, totalEpisodes);
        SelectedWatchedEpisodes = Math.Clamp(watchedEpisodes, 0, SelectedWatchTotalEpisodes);
        OnPropertyChanged(nameof(ShowWatchEpisodeControls));
        OnPropertyChanged(nameof(SelectedWatchEpisodesLabel));
        OnPropertyChanged(nameof(CanIncrementWatchedEpisodes));
        OnPropertyChanged(nameof(CanDecrementWatchedEpisodes));
        IncrementWatchedEpisodesCommand.NotifyCanExecuteChanged();
        DecrementWatchedEpisodesCommand.NotifyCanExecuteChanged();

        // Defer clearing suppress so stale ComboBox SelectedValue write-backs after
        // selection/filter changes are ignored.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () =>
            {
                if (generation == _watchProgressSuppressGeneration)
                {
                    _suppressWatchProgressUpdate = false;
                }
            },
            DispatcherPriority.Background);
    }

    private void SetRatingThoughtUi(double? rating, string? thought)
    {
        _suppressRatingThoughtUpdate = true;
        IsThoughtPopupOpen = false;
        IsEditingThought = false;
        SelectedRating = ClampRating(rating ?? 0);
        SelectedThought = NormalizeThought(thought);
        OnPropertyChanged(nameof(SelectedRatingText));
        OnPropertyChanged(nameof(CanIncrementRating));
        OnPropertyChanged(nameof(CanDecrementRating));
        OnPropertyChanged(nameof(ShowThoughtDisplay));
        OnPropertyChanged(nameof(ShowThoughtEditor));
        OnPropertyChanged(nameof(HasThoughtText));
        OnPropertyChanged(nameof(ThoughtDisplayText));
        IncrementRatingCommand.NotifyCanExecuteChanged();
        DecrementRatingCommand.NotifyCanExecuteChanged();
        BeginEditThoughtCommand.NotifyCanExecuteChanged();
        ToggleThoughtPopupCommand.NotifyCanExecuteChanged();
        _suppressRatingThoughtUpdate = false;
    }

    private void PersistSelectedRatingAndThought()
    {
        if (_suppressRatingThoughtUpdate || !HasSelectedMedia)
        {
            return;
        }

        var rating = ClampRating(SelectedRating);
        var thought = NormalizeThought(SelectedThought);
        var thoughtForDb = string.IsNullOrWhiteSpace(thought) ? null : thought;

        if (SelectedShow is not null)
        {
            _trackedShowService.UpdateRating(SelectedShow.Id, rating, thoughtForDb);
            SelectedMediaCard?.ApplyRating(rating);
            SyncCardInAllMedia(SelectedMediaCard);
            ApplyMediaCardFilterAndSort();
            StatusMessage = $"Rating/thought updated: {rating:0.0}.";
            return;
        }

        if (SelectedMovie is not null)
        {
            _trackedMovieService.UpdateRating(SelectedMovie.Id, rating, thoughtForDb);
            SelectedMediaCard?.ApplyRating(rating);
            SyncCardInAllMedia(SelectedMediaCard);
            ApplyMediaCardFilterAndSort();
            StatusMessage = $"Rating/thought updated: {rating:0.0}.";
        }
    }

    private static double ClampRating(double value) =>
        Math.Clamp(Math.Round(value, 1, MidpointRounding.AwayFromZero), RatingMin, RatingMax);

    private static string NormalizeThought(string? thought)
    {
        if (string.IsNullOrWhiteSpace(thought))
        {
            return string.Empty;
        }

        var trimmed = thought.Trim();
        return trimmed.Length <= ThoughtMaxLength
            ? trimmed
            : trimmed[..ThoughtMaxLength];
    }

    private void PersistSelectedWatchProgress(UserWatchStatus status, int watchedEpisodes)
    {
        if (SelectedShow is not null)
        {
            var total = SelectedWatchTotalEpisodes;
            var clampedWatched = Math.Clamp(watchedEpisodes, 0, Math.Max(0, total));
            _trackedShowService.UpdateWatchProgress(SelectedShow.Id, status, clampedWatched);
            SetWatchProgressUi(status, clampedWatched, total);
            SelectedMediaCard?.ApplyWatchProgress(status, clampedWatched);
            SyncCardInAllMedia(SelectedMediaCard);
            StatusMessage = $"Watch progress updated: {TrackedShow.FormatWatchStatusLabel(status)}, {clampedWatched}/{total}.";
            return;
        }

        if (SelectedMovie is not null)
        {
            _trackedMovieService.UpdateWatchStatus(SelectedMovie.Id, status);
            SetWatchProgressUi(status, watchedEpisodes: 0, totalEpisodes: 0);
            SelectedMediaCard?.ApplyWatchProgress(status, watched: 0);
            SyncCardInAllMedia(SelectedMediaCard);
            StatusMessage = $"Watch status updated: {TrackedShow.FormatWatchStatusLabel(status)}.";
        }
    }

    private void SyncCardInAllMedia(LibraryMediaCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var catalogCard = _allMediaCards.FirstOrDefault(item => item.Id == card.Id && item.MediaKind == card.MediaKind);
        catalogCard?.ApplyWatchProgress(card.WatchStatus, card.WatchedEpisodes);
        catalogCard?.ApplyRating(card.Rating);

        if (WatchStatusFilterOption.GetSelectedStatuses(WatchStatusFilterOptions).Count > 0)
        {
            ApplyMediaCardFilterAndSort();
        }
    }

    private async Task RunImportActionAsync(Func<Task> action)
    {
        if (IsImportBusy)
        {
            return;
        }

        try
        {
            IsImportBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            ImportStatusMessage = ex.Message;
            StatusMessage = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsImportBusy = false;
            NotifyImportStateChanged();
        }
    }

    private void RefreshImportGroupTabs()
    {
        ReadyImportGroups.Clear();
        NeedsReviewImportGroups.Clear();
        IgnoredImportGroups.Clear();

        foreach (var group in ImportGroups)
        {
            switch (group.Status)
            {
                case MediaImportGroupStatus.Ready:
                    ReadyImportGroups.Add(group);
                    break;
                case MediaImportGroupStatus.NeedsReview:
                    NeedsReviewImportGroups.Add(group);
                    break;
                case MediaImportGroupStatus.Ignored:
                    IgnoredImportGroups.Add(group);
                    break;
            }
        }

        NotifyImportStateChanged();
    }

    private void SelectImportedMedia(MediaImportCommitResult result)
    {
        var firstImported = result.ImportedMedia.FirstOrDefault();
        if (firstImported.MediaId <= 0)
        {
            return;
        }

        SelectedMediaCard = MediaCards.FirstOrDefault(card =>
            card.MediaKind == firstImported.MediaKind &&
            card.Id == firstImported.MediaId);
    }

    private void NotifyImportStateChanged()
    {
        OnPropertyChanged(nameof(HasImportFolders));
        OnPropertyChanged(nameof(HasImportGroups));
        OnPropertyChanged(nameof(CanScanImportFolders));
        OnPropertyChanged(nameof(CanImportSelected));
        ScanImportFoldersCommand.NotifyCanExecuteChanged();
        ImportSelectedCommand.NotifyCanExecuteChanged();
    }

    private static string? BrowseFolder(string description)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void UpdateSeasonManagementMode(long showId, int seasonNumber, SeasonManagementMode mode)
    {
        _trackedShowService.UpdateSeasonPackMode(showId, seasonNumber, mode);
        AddEpisodeToCartCommand.NotifyCanExecuteChanged();
        AddSeasonPackToCartCommand.NotifyCanExecuteChanged();
        LinkEpisodeCommand.NotifyCanExecuteChanged();
        RuleLinkSeasonPackCommand.NotifyCanExecuteChanged();
        AiLinkSeasonPackCommand.NotifyCanExecuteChanged();
        UnlinkSeasonPackCommand.NotifyCanExecuteChanged();
        CleanupSeasonPackCommand.NotifyCanExecuteChanged();
        StatusMessage = $"Season {seasonNumber:00} set to {mode} mode.";
    }

    private List<LibraryEpisodeRowViewModel> AppendOrphanPackRows(
        long showId,
        int tmdbId,
        List<LibraryEpisodeRowViewModel> trackedRows,
        IReadOnlyList<SourceItem> sourceItems)
    {
        var providerId = tmdbId.ToString();
        var orphanItems = sourceItems
            .Where(item =>
                item.IsOrphanPackSpecial &&
                item.MatchAccepted &&
                item.State == ItemState.Linked &&
                !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                File.Exists(item.LinkedPath) &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (orphanItems.Count == 0)
        {
            return trackedRows;
        }

        var rows = new List<LibraryEpisodeRowViewModel>(trackedRows);
        rows.Add(LibraryEpisodeRowViewModel.CreateOrphanSeparator());
        rows.AddRange(orphanItems.Select(item => LibraryEpisodeRowViewModel.CreateOrphan(item, showId)));
        return rows;
    }

    private Dictionary<(int SeasonNumber, int EpisodeNumber), string> GetLinkedEpisodeStatuses(
        int tmdbId,
        IReadOnlyList<SourceItem> sourceItems)
    {
        var providerId = tmdbId.ToString();
        var statuses = new Dictionary<(int SeasonNumber, int EpisodeNumber), string>();
        foreach (var item in sourceItems
                     .Where(item =>
                         item.MediaKind == MediaKind.TvEpisode &&
                         !item.IsOrphanPackSpecial &&
                         item.MatchAccepted &&
                         item.State == ItemState.Linked &&
                         !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                         File.Exists(item.LinkedPath) &&
                         string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)))
        {
            var season = item.MappedSeasonNumber ?? item.SeasonNumber;
            var episode = item.MappedEpisodeNumber ?? item.EpisodeNumber;
            if (season is null || episode is null)
            {
                continue;
            }

            var key = (season.Value, episode.Value);
            var status = item.AutoTorrentLinkKind switch
            {
                AutoTorrentLinkKind.SeasonPack when item.AutoTorrentPackOwnerSeasonNumber is not null =>
                    $"Linked by pack S{item.AutoTorrentPackOwnerSeasonNumber.Value:00}",
                AutoTorrentLinkKind.Episode => "Linked by episode torrent",
                _ => "Linked"
            };

            if (!statuses.TryGetValue(key, out var existingStatus) ||
                GetLinkStatusPriority(status) > GetLinkStatusPriority(existingStatus))
            {
                statuses[key] = status;
            }
        }

        return statuses;
    }

    private HashSet<int> GetLinkedPackOwnerSeasons(int tmdbId, IReadOnlyList<SourceItem> sourceItems)
    {
        var providerId = tmdbId.ToString();
        return sourceItems
            .Where(item =>
                item.MediaKind == MediaKind.TvEpisode &&
                item.MatchAccepted &&
                item.State == ItemState.Linked &&
                item.AutoTorrentLinkKind == AutoTorrentLinkKind.SeasonPack &&
                item.AutoTorrentPackOwnerSeasonNumber is not null &&
                !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                File.Exists(item.LinkedPath) &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.AutoTorrentPackOwnerSeasonNumber!.Value)
            .ToHashSet();
    }

    private static int GetLinkStatusPriority(string status)
    {
        if (status.StartsWith("Linked by pack", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return string.Equals(status, "Linked by episode torrent", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    }

    private static bool IsMovieLinked(int tmdbId, IReadOnlyList<SourceItem> sourceItems)
    {
        var providerId = tmdbId.ToString();
        return sourceItems.Any(item =>
            item.MediaKind == MediaKind.Movie &&
            item.MatchAccepted &&
            item.State == ItemState.Linked &&
            !string.IsNullOrWhiteSpace(item.LinkedPath) &&
            File.Exists(item.LinkedPath) &&
            string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanAddMovieToCart(LibraryMovieDetailViewModel? movie) => movie?.CanAddToCart == true;

    private bool CanAddEpisodeToCart(LibraryEpisodeRowViewModel? episode) => episode?.CanAddToCart == true;

    private bool CanAddSeasonPackToCart(LibrarySeasonViewModel? season) => season?.CanAddPackToCart == true;

    private bool CanLinkMovie(LibraryMovieDetailViewModel? movie) => movie?.CanLink == true;

    private bool CanResetMovie(LibraryMovieDetailViewModel? movie) => movie?.CanReset == true;

    private bool CanLinkEpisode(LibraryEpisodeRowViewModel? episode) => episode?.CanLink == true;

    private bool CanRuleLinkSeasonPack(LibrarySeasonViewModel? season) => season?.CanRuleLinkPack == true;

    private bool CanAiLinkSeasonPack(LibrarySeasonViewModel? season) => season?.CanAiLinkPack == true;

    private bool CanUnlinkSeasonPack(LibrarySeasonViewModel? season) => season?.CanUnlinkPack == true;

    private bool CanCleanupSeasonPack(LibrarySeasonViewModel? season) => season?.CanCleanupPack == true;

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
