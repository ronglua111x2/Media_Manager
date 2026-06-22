using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
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

    private IReadOnlyList<LibraryMediaCardViewModel> _allMediaCards = [];
    private long? _loadedDetailMediaId;
    private MediaKind? _loadedDetailMediaKind;
    private bool _suppressSeriesStatusUpdate;

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
        IMediaImportService mediaImportService,
        IMediaMetadataSyncService mediaMetadataSyncService,
        ISettingsService settingsService,
        IDownloadFolderCatalogService downloadFolderCatalogService,
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
        _torrentReconciliationService.Reconciled += (_, _) => _ = ReloadSelectedDetailAsync();
        lifecycleService.AppModeChanged += OnAppModeChanged;
        RefreshLibrary();
        StatusMessage = "Select a media card to view details.";
    }

    public ObservableCollection<LibraryMediaCardViewModel> MediaCards { get; } = [];

    public ObservableCollection<string> ImportFolders { get; } = [];

    public ObservableCollection<MediaImportGroupViewModel> ImportGroups { get; } = [];

    public ObservableCollection<MediaImportGroupViewModel> ReadyImportGroups { get; } = [];

    public ObservableCollection<MediaImportGroupViewModel> NeedsReviewImportGroups { get; } = [];

    public ObservableCollection<MediaImportGroupViewModel> IgnoredImportGroups { get; } = [];

    public IReadOnlyList<MediaCardSortMode> SortModes { get; } =
    [
        MediaCardSortMode.DateAddedDesc,
        MediaCardSortMode.TypeThenTitle,
        MediaCardSortMode.Title
    ];

    [ObservableProperty]
    private LibraryMediaCardViewModel? selectedMediaCard;

    [ObservableProperty]
    private LibraryShowDetailViewModel? selectedShow;

    [ObservableProperty]
    private LibraryMovieDetailViewModel? selectedMovie;

    [ObservableProperty]
    private ShowSeriesStatus selectedShowSeriesStatus;

    [ObservableProperty]
    private ImageSource? selectedPosterImage;

    [ObservableProperty]
    private MediaCardSortMode mediaSortMode = MediaCardSortMode.DateAddedDesc;

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

    public bool HasMedia => MediaCards.Count > 0;

    public bool HasSelectedMedia => SelectedMediaCard is not null;

    public bool IsSelectedShow => SelectedShow is not null;

    public bool IsSelectedMovie => SelectedMovie is not null;

    public bool ShowStopAutoTrackButton => IsSelectedShow && SelectedShow?.IsAutoTracked == true;

    public bool IsDateSortSelected => MediaSortMode == MediaCardSortMode.DateAddedDesc;

    public bool IsTypeSortSelected => MediaSortMode == MediaCardSortMode.TypeThenTitle;

    public bool IsNameSortSelected => MediaSortMode == MediaCardSortMode.Title;

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
        ApplyMediaCardSort();

        if (selectedId is not null && selectedKind is not null)
        {
            SelectedMediaCard = MediaCards.FirstOrDefault(card => card.Id == selectedId && card.MediaKind == selectedKind);
        }

        SelectedMediaCard ??= MediaCards.FirstOrDefault();
        OnPropertyChanged(nameof(HasMedia));
        StatusMessage = MediaCards.Count == 0
            ? "No media in library. Use Find/Add to add shows or movies."
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

    [RelayCommand(CanExecute = nameof(CanLinkSeasonPack))]
    private async Task LinkSeasonPack(LibrarySeasonViewModel? season)
    {
        if (season is null)
        {
            return;
        }

        try
        {
            if (season.IsPackLinked)
            {
                StatusMessage = $"Removing library links for season {season.SeasonNumber:00} pack...";
                var unlinkResult = _autoTorrentLinkService.RemoveSeasonPackLinks(season.ShowId, season.SeasonNumber);
                _trackedShowService.RefreshAvailability(season.ShowId);
                await ReloadSelectedDetailAsync();
                StatusMessage = $"Removed pack library links for S{season.SeasonNumber:00}: {unlinkResult.Summary}.";
                return;
            }

            StatusMessage = $"Creating library links for season {season.SeasonNumber:00} pack...";
            var result = await _autoTorrentLinkService.LinkSeasonPackAsync(season.ShowId, season.SeasonNumber);
            _trackedShowService.RefreshAvailability(season.ShowId);
            await ReloadSelectedDetailAsync();
            StatusMessage = $"Pack library links for S{season.SeasonNumber:00}: {result.Summary}.";
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

    [RelayCommand]
    private async Task ReconcileExistingTorrents()
    {
        try
        {
            var scope = SelectedMediaCard is null
                ? TorrentReconciliationScope.All
                : TorrentReconciliationScope.ForMedia(SelectedMediaCard.MediaKind, SelectedMediaCard.Id);
            StatusMessage = SelectedMediaCard is null
                ? "Reconciling existing qBittorrent torrents..."
                : $"Reconciling existing qBittorrent torrents for {SelectedMediaCard.Title}...";

            var result = await _torrentReconciliationService.ReconcileAsync(scope);
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
                    ? $"TMDB refresh complete for {result.Title}. {result.NewEpisodesAdded} new episode(s) added."
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
        if (episodes.Count == 0)
        {
            StatusMessage = "No episodes available. Sync from TMDB first.";
            return;
        }

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
            show.IsAutoTracked ? show.AutoTrackAutoReconcileAndLink : true)
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

    [RelayCommand(CanExecute = nameof(HasSelectedMedia))]
    private void DeleteSelectedMedia()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Delete '{SelectedMediaCard.Title}' from library?\n\nThis removes hardlinks, seasons/episodes, and clears its cart.",
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

    [RelayCommand(CanExecute = nameof(HasMedia))]
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

        OnPropertyChanged(nameof(HasSelectedMedia));
        OnPropertyChanged(nameof(IsSelectedShow));
        OnPropertyChanged(nameof(IsSelectedMovie));
        OnPropertyChanged(nameof(ShowStopAutoTrackButton));
    }

    partial void OnMediaSortModeChanged(MediaCardSortMode value)
    {
        ApplyMediaCardSort();
        OnPropertyChanged(nameof(IsDateSortSelected));
        OnPropertyChanged(nameof(IsTypeSortSelected));
        OnPropertyChanged(nameof(IsNameSortSelected));
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
            return;
        }

        if (card.IsShow)
        {
            var show = _trackedShowService.GetShows().FirstOrDefault(item => item.Id == card.Id);
            if (show is null)
            {
                StatusMessage = "Selected show was not found.";
                return;
            }

            SelectedShow = BuildShowDetail(show, expandedSeasons);
            _suppressSeriesStatusUpdate = true;
            SelectedShowSeriesStatus = show.SeriesStatus;
            _suppressSeriesStatusUpdate = false;
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

        SelectedMovie = BuildMovieDetail(movie);
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
        LinkEpisodeCommand.NotifyCanExecuteChanged();
        LinkSeasonPackCommand.NotifyCanExecuteChanged();
    }

    private async Task ReloadSelectedDetailAsync()
    {
        await LoadSelectedMediaAsync(SelectedMediaCard);
        AddMovieToCartCommand.NotifyCanExecuteChanged();
        AddEpisodeToCartCommand.NotifyCanExecuteChanged();
        AddSeasonPackToCartCommand.NotifyCanExecuteChanged();
        LinkMovieCommand.NotifyCanExecuteChanged();
        LinkEpisodeCommand.NotifyCanExecuteChanged();
        LinkSeasonPackCommand.NotifyCanExecuteChanged();
    }

    private LibraryShowDetailViewModel BuildShowDetail(TrackedShow show, IReadOnlySet<int>? expandedSeasons = null)
    {
        var linkedEpisodeStatuses = GetLinkedEpisodeStatuses(show.TmdbId);
        var linkedPackOwnerSeasons = GetLinkedPackOwnerSeasons(show.TmdbId);
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

        var seasons = episodes
            .GroupBy(episode => episode.SeasonNumber)
            .Where(group => ShowHiddenSeasons || !hiddenSeasonNumbers.Contains(group.Key))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                seasonRecords.TryGetValue(group.Key, out var seasonRecord);
                return new LibrarySeasonViewModel(
                    show.Id,
                    group.Key,
                    group,
                    UpdateSeasonManagementMode,
                    seasonRecord)
                {
                    IsExpanded = expandedSeasons?.Contains(group.Key) == true,
                    IsPackInCart = _torrentCartService.TryGetActiveSeasonPackOrder(show.Id, group.Key, out _),
                    IsPackLinked = linkedPackOwnerSeasons.Contains(group.Key)
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

        SelectedShow = BuildShowDetail(show, expandedSeasons);
        _suppressSeriesStatusUpdate = true;
        SelectedShowSeriesStatus = show.SeriesStatus;
        _suppressSeriesStatusUpdate = false;

        if (ShowHiddenSeasons && SelectedShow?.HasHiddenSeasons != true)
        {
            ShowHiddenSeasons = false;
        }

        OnPropertyChanged(nameof(ShowStopAutoTrackButton));
    }

    private LibraryMovieDetailViewModel BuildMovieDetail(TrackedMovie movie)
    {
        return new LibraryMovieDetailViewModel(movie)
        {
            LibraryLinkStatus = IsMovieLinked(movie.TmdbId) ? "Linked" : "Not linked",
            IsInCart = _torrentCartService.TryGetActiveMovieOrder(movie.Id, out _)
        };
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
        LinkSeasonPackCommand.NotifyCanExecuteChanged();
        StatusMessage = $"Season {seasonNumber:00} set to {mode} mode.";
    }

    private Dictionary<(int SeasonNumber, int EpisodeNumber), string> GetLinkedEpisodeStatuses(int tmdbId)
    {
        var providerId = tmdbId.ToString();
        var statuses = new Dictionary<(int SeasonNumber, int EpisodeNumber), string>();
        foreach (var item in _databaseService.GetSourceItems()
                     .Where(item =>
                         item.MediaKind == MediaKind.TvEpisode &&
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

    private HashSet<int> GetLinkedPackOwnerSeasons(int tmdbId)
    {
        var providerId = tmdbId.ToString();
        return _databaseService.GetSourceItems()
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

    private bool IsMovieLinked(int tmdbId)
    {
        var providerId = tmdbId.ToString();
        return _databaseService.GetSourceItems().Any(item =>
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

    private bool CanLinkEpisode(LibraryEpisodeRowViewModel? episode) => episode?.CanLink == true;

    private bool CanLinkSeasonPack(LibrarySeasonViewModel? season) => season?.CanLinkPack == true;

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
