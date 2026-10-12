using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.Services.Backup;
using media_management_app.Services.Gemini;
using WinForms = System.Windows.Forms;

namespace media_management_app.ViewModels;

public sealed partial class FirstRunViewModel : ObservableObject
{
    private static readonly FirstRunStepKind[] MustSteps =
    [
        FirstRunStepKind.StateFolder,
        FirstRunStepKind.Folders
    ];

    private readonly IHostScanService _hostScanService;
    private readonly ISettingsService _settingsService;
    private readonly IBackupService _backupService;
    private readonly AppLaunchOptions _launchOptions;
    private readonly IAppLogger _logger;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IQbittorrentSearchPluginService _searchPlugins;
    private readonly IJellyfinClient _jellyfinClient;
    private readonly IWarpCliService _warpCliService;
    private readonly IGeminiApiClient _geminiApiClient;
    private readonly IGoogleDriveClient _googleDriveClient;
    private readonly ILibraryPathResolver _libraryPathResolver;
    private readonly IThemeService _themeService;
    private readonly IWindowsStartupService _windowsStartupService;
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly HttpClient _httpClient;
    private readonly HostScanOverrides _overrides = new();
    private readonly Dictionary<HostScanComponentId, HostScanRow> _scanById = [];
    private readonly HashSet<FirstRunStepKind> _satisfied = [];
    private readonly HashSet<FirstRunStepKind> _skipped = [];
    private List<FirstRunStepKind> _pipeline = [];
    private bool _jellyfinSkipped;
    private bool _restoreApplied;
    private CancellationTokenSource? _warmupSearchCts;

    public FirstRunViewModel(
        IHostScanService hostScanService,
        ISettingsService settingsService,
        IBackupService backupService,
        AppLaunchOptions launchOptions,
        IAppLogger logger,
        IQbittorrentClient qbittorrentClient,
        IQbittorrentSearchPluginService searchPlugins,
        IJellyfinClient jellyfinClient,
        IWarpCliService warpCliService,
        IGeminiApiClient geminiApiClient,
        IGoogleDriveClient googleDriveClient,
        ILibraryPathResolver libraryPathResolver,
        IThemeService themeService,
        IWindowsStartupService windowsStartupService,
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        HttpClient httpClient)
    {
        _hostScanService = hostScanService;
        _settingsService = settingsService;
        _backupService = backupService;
        _launchOptions = launchOptions;
        _logger = logger;
        _qbittorrentClient = qbittorrentClient;
        _searchPlugins = searchPlugins;
        _jellyfinClient = jellyfinClient;
        _warpCliService = warpCliService;
        _geminiApiClient = geminiApiClient;
        _googleDriveClient = googleDriveClient;
        _libraryPathResolver = libraryPathResolver;
        _themeService = themeService;
        _windowsStartupService = windowsStartupService;
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _httpClient = httpClient;

        IsGated = settingsService.Current.Startup?.SetupCompleted != true;
        LoadFromSettings();
        RebuildPipeline();
        ApplyCurrentStep();
    }

    public ObservableCollection<FirstRunRailRow> RailRows { get; } = [];

    public ObservableCollection<TmdbUnifiedSearchResult> WarmupResults { get; } = [];

    public ObservableCollection<string> WarmupAddedTitles { get; } = [];

    public AppTheme[] ThemeOptions { get; } = [AppTheme.Light, AppTheme.Dark];

    public DayOfWeek[] AnchorDays { get; } = Enum.GetValues<DayOfWeek>();

    public event EventHandler? CloseRequested;

    public event EventHandler? QuitRequested;

    public event EventHandler? RestartRequested;

    [ObservableProperty]
    private bool isGated;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isContinuing;

    [ObservableProperty]
    private bool suppressClosePrompt;

    [ObservableProperty]
    private string statusMessage = "Checking this PC…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartStep))]
    [NotifyPropertyChangedFor(nameof(IsRestoreStep))]
    [NotifyPropertyChangedFor(nameof(IsStateFolderStep))]
    [NotifyPropertyChangedFor(nameof(IsTmdbStep))]
    [NotifyPropertyChangedFor(nameof(IsQbittorrentStep))]
    [NotifyPropertyChangedFor(nameof(IsFoldersStep))]
    [NotifyPropertyChangedFor(nameof(IsJellyfinStep))]
    [NotifyPropertyChangedFor(nameof(IsSymlinkStep))]
    [NotifyPropertyChangedFor(nameof(IsWarpStep))]
    [NotifyPropertyChangedFor(nameof(IsGeminiStep))]
    [NotifyPropertyChangedFor(nameof(IsDriveStep))]
    [NotifyPropertyChangedFor(nameof(IsWindowsStep))]
    [NotifyPropertyChangedFor(nameof(IsWarmupStep))]
    [NotifyPropertyChangedFor(nameof(IsAutoTrackStep))]
    [NotifyPropertyChangedFor(nameof(StepProgressText))]
    [NotifyPropertyChangedFor(nameof(StepSectionTitle))]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    [NotifyPropertyChangedFor(nameof(StepWhy))]
    [NotifyPropertyChangedFor(nameof(StepHint))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowSkip))]
    [NotifyPropertyChangedFor(nameof(ShowTest))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(CanBrowseStateFolder))]
    private FirstRunStepKind currentStep = FirstRunStepKind.Start;

    [ObservableProperty]
    private int stepIndex;

    [ObservableProperty]
    private int stepCount = 1;

    [ObservableProperty]
    private string stateFolder = string.Empty;

    [ObservableProperty]
    private string? tmdbToken;

    [ObservableProperty]
    private string qbittorrentUrl = HostSoftwareCatalog.QbittorrentDefaultWebUiUrl;

    [ObservableProperty]
    private string? qbittorrentUsername;

    [ObservableProperty]
    private string? qbittorrentPassword;

    [ObservableProperty]
    private string? qbittorrentApiKey;

    [ObservableProperty]
    private string downloadFolder = string.Empty;

    [ObservableProperty]
    private bool useDownloadFolderAsSource = true;

    [ObservableProperty]
    private string sourceFolder = string.Empty;

    [ObservableProperty]
    private string libraryPreview = string.Empty;

    [ObservableProperty]
    private string jellyfinUrl = HostSoftwareCatalog.JellyfinDefaultBaseUrl;

    [ObservableProperty]
    private string? jellyfinApiKey;

    [ObservableProperty]
    private string symlinkRoot = AppConstants.DefaultSymlinkUnifiedRoot;

    [ObservableProperty]
    private string warpCliPath = AppConstants.DefaultWarpCliPath;

    [ObservableProperty]
    private string? geminiApiKey;

    [ObservableProperty]
    private string driveCredentialsPath = string.Empty;

    [ObservableProperty]
    private AppTheme selectedTheme = AppTheme.Light;

    [ObservableProperty]
    private bool runAtStartup;

    [ObservableProperty]
    private bool closeToTray;

    [ObservableProperty]
    private bool autoTrackEnabled;

    [ObservableProperty]
    private DayOfWeek autoTrackAnchorDay = DayOfWeek.Sunday;

    [ObservableProperty]
    private string autoTrackAnchorTime = "21:00";

    [ObservableProperty]
    private string pathWarning = string.Empty;

    [ObservableProperty]
    private bool wantRestore;

    [ObservableProperty]
    private string restorePath = string.Empty;

    [ObservableProperty]
    private string warmupQuery = string.Empty;

    [ObservableProperty]
    private TmdbUnifiedSearchResult? selectedWarmupResult;

    [ObservableProperty]
    private bool isWarmupSearching;

    [ObservableProperty]
    private bool isWarmupAdding;

    [ObservableProperty]
    private bool statusIsError;

    public bool IsStartStep => CurrentStep == FirstRunStepKind.Start;
    public bool IsRestoreStep => CurrentStep == FirstRunStepKind.Restore;
    public bool IsStateFolderStep => CurrentStep == FirstRunStepKind.StateFolder;
    public bool IsTmdbStep => CurrentStep == FirstRunStepKind.Tmdb;
    public bool IsQbittorrentStep => CurrentStep == FirstRunStepKind.Qbittorrent;
    public bool IsFoldersStep => CurrentStep == FirstRunStepKind.Folders;
    public bool IsJellyfinStep => CurrentStep == FirstRunStepKind.Jellyfin;
    public bool IsSymlinkStep => CurrentStep == FirstRunStepKind.Symlink;
    public bool IsWarpStep => CurrentStep == FirstRunStepKind.Warp;
    public bool IsGeminiStep => CurrentStep == FirstRunStepKind.Gemini;
    public bool IsDriveStep => CurrentStep == FirstRunStepKind.Drive;
    public bool IsWindowsStep => CurrentStep == FirstRunStepKind.Windows;
    public bool IsWarmupStep => CurrentStep == FirstRunStepKind.Warmup;
    public bool IsAutoTrackStep => CurrentStep == FirstRunStepKind.AutoTrack;

    public bool IsStateFolderPinned => _launchOptions.HasStateFolderOverride;

    public bool CanBrowseStateFolder => !IsStateFolderPinned;

    public bool WantFresh
    {
        get => !WantRestore;
        set
        {
            if (value)
            {
                WantRestore = false;
            }
        }
    }

    public string StepProgressText => $"Step {StepIndex + 1} of {StepCount}";

    public string StepSectionTitle => GroupFor(CurrentStep);

    public string StepTitle => TitleFor(CurrentStep);

    public string StepWhy => CurrentStep switch
    {
        FirstRunStepKind.Start => "Fresh sets up this PC. Restore copies a local zip or copied state folder into the state folder you pick next.",
        FirstRunStepKind.Restore => "Pick a backup zip or a copied state folder. Next copies the database and recipes into this PC's state folder.",
        FirstRunStepKind.StateFolder => "This is where the library database, settings, and posters live.",
        FirstRunStepKind.Tmdb => "Lets the app search and track shows and movies. Skip if TMDB is blocked (some networks need WARP).",
        FirstRunStepKind.Qbittorrent => "Lets the app add torrents and hunt episodes. Skip if qBittorrent is not ready.",
        FirstRunStepKind.Folders => "Downloads land here; the library reads source folders (same path can be both).",
        FirstRunStepKind.Jellyfin => "Open titles in Jellyfin and refresh its library.",
        FirstRunStepKind.Symlink => "Jellyfin can see a single library folder of links.",
        FirstRunStepKind.Warp => "Helps TMDB and Jellyfin when the network needs WARP.",
        FirstRunStepKind.Gemini => "Maps specials when filenames are messy.",
        FirstRunStepKind.Drive => "Uploads a backup zip of this machine’s state.",
        FirstRunStepKind.Windows => "How the app looks and starts.",
        FirstRunStepKind.Warmup => "Add a few shows or movies so News and Auto are not empty. Skip is fine.",
        FirstRunStepKind.AutoTrack => "Hunt new episodes on a schedule. Leave this off until you are ready.",
        _ => string.Empty
    };

    public string StepHint
    {
        get
        {
            if (CurrentStep == FirstRunStepKind.StateFolder && IsStateFolderPinned)
            {
                return "This launch is pinned by --state-folder. The path cannot be changed until you start without that flag.";
            }

            var row = CurrentStep switch
            {
                FirstRunStepKind.Qbittorrent => GetScan(HostScanComponentId.Qbittorrent),
                FirstRunStepKind.Warp => GetScan(HostScanComponentId.Warp),
                FirstRunStepKind.Jellyfin => GetScan(HostScanComponentId.Jellyfin),
                FirstRunStepKind.Tmdb => GetScan(HostScanComponentId.Tmdb),
                FirstRunStepKind.Gemini => GetScan(HostScanComponentId.Gemini),
                FirstRunStepKind.Drive => GetScan(HostScanComponentId.Drive),
                _ => null
            };
            if (row is null)
            {
                return string.Empty;
            }

            if (row.Usable == HostScanCheckStatus.NotConfigured ||
                row.Installed == HostScanCheckStatus.NotConfigured ||
                string.Equals(row.Note, HostSoftwareCatalog.NotConfiguredNote, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(row.DetectedVersion) && string.IsNullOrWhiteSpace(row.Path))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(row.Note) ||
                string.Equals(row.Note, HostSoftwareCatalog.NotConfiguredNote, StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(row.DetectedVersion)
                    ? row.Path ?? string.Empty
                    : $"Found {row.DetectedVersion}.";
            }

            return row.Note;
        }
    }

    public bool IsLastStep => StepIndex >= StepCount - 1;

    public bool ShowSkip => CurrentStep is not FirstRunStepKind.Start && !MustSteps.Contains(CurrentStep);

    public bool ShowTest => CurrentStep is FirstRunStepKind.Tmdb
        or FirstRunStepKind.Qbittorrent
        or FirstRunStepKind.Jellyfin
        or FirstRunStepKind.Warp
        or FirstRunStepKind.Gemini
        or FirstRunStepKind.Drive;

    public string PrimaryButtonText => IsLastStep ? "Finish" : "Next";

    private bool CanBack => StepIndex > 0 && !IsBusy && !IsContinuing;

    private bool CanSkip => ShowSkip && !IsBusy && !IsContinuing;

    private bool CanNext
    {
        get
        {
            if (IsBusy || IsContinuing)
            {
                return false;
            }

            return CanAdvanceCurrentStep();
        }
    }

    private bool CanAdvanceCurrentStep() => CurrentStep switch
    {
        FirstRunStepKind.Start => true,
        FirstRunStepKind.Restore => !string.IsNullOrWhiteSpace(RestorePath),
        _ => ShowSkip || IsMustSatisfied(CurrentStep)
    };

    private bool CanTest => !IsBusy && !IsContinuing;

    private bool CanSearchWarmup => !IsBusy && !IsContinuing && !IsWarmupSearching && !string.IsNullOrWhiteSpace(WarmupQuery);

    private bool CanAddWarmup => !IsBusy && !IsContinuing && !IsWarmupAdding && SelectedWarmupResult is { IsAlreadyAdded: false };

    partial void OnIsBusyChanged(bool value) => NotifyNavigation();

    partial void OnIsContinuingChanged(bool value) => NotifyNavigation();

    partial void OnWantRestoreChanged(bool value)
    {
        OnPropertyChanged(nameof(WantFresh));
        RebuildPipeline();
        RefreshRail();
        NotifyNavigation();
    }

    partial void OnRestorePathChanged(string value) => NotifyNavigation();

    partial void OnWarmupQueryChanged(string value) => SearchWarmupCommand.NotifyCanExecuteChanged();

    partial void OnSelectedWarmupResultChanged(TmdbUnifiedSearchResult? value) => AddWarmupCommand.NotifyCanExecuteChanged();

    partial void OnIsWarmupSearchingChanged(bool value) => SearchWarmupCommand.NotifyCanExecuteChanged();

    partial void OnIsWarmupAddingChanged(bool value) => AddWarmupCommand.NotifyCanExecuteChanged();

    partial void OnDownloadFolderChanged(string value)
    {
        if (UseDownloadFolderAsSource)
        {
            SourceFolder = value;
        }

        RefreshFolderPreview();
        EvaluateFolders();
        NotifyNavigation();
    }

    partial void OnSourceFolderChanged(string value)
    {
        RefreshFolderPreview();
        EvaluateFolders();
        NotifyNavigation();
    }

    partial void OnUseDownloadFolderAsSourceChanged(bool value)
    {
        if (value && !string.IsNullOrWhiteSpace(DownloadFolder))
        {
            SourceFolder = DownloadFolder;
        }

        EvaluateFolders();
        NotifyNavigation();
    }

    partial void OnStateFolderChanged(string value)
    {
        EvaluateStateFolder();
        NotifyNavigation();
    }

    partial void OnSelectedThemeChanged(AppTheme value) => _themeService.Apply(value);

    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsBusy = true;
        StatusMessage = "Checking this PC…";
        try
        {
            var rows = await _hostScanService.ScanAsync(_overrides);
            _scanById.Clear();
            foreach (var row in rows)
            {
                _scanById[row.Id] = row;
            }

            OnPropertyChanged(nameof(StepHint));
            ApplyStepStatusMessage();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back()
    {
        if (StepIndex <= 0)
        {
            return;
        }

        StepIndex--;
        ApplyCurrentStep();
    }

    [RelayCommand(CanExecute = nameof(CanSkip))]
    private async Task Skip()
    {
        MarkSkipped(CurrentStep);
        await AdvanceAsync();
    }

    [RelayCommand(CanExecute = nameof(CanNext))]
    private async Task Next()
    {
        if (!CanAdvanceCurrentStep())
        {
            return;
        }

        if (CurrentStep == FirstRunStepKind.Restore)
        {
            if (!await TryApplyRestoreAsync())
            {
                return;
            }
        }
        else if (ShowSkip && !IsFilledOptional(CurrentStep))
        {
            _skipped.Add(CurrentStep);
        }

        await AdvanceAsync();
    }

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        ApplyWorkingSettings();
        IsBusy = true;
        StatusIsError = false;
        try
        {
            StatusMessage = "Testing…";
            switch (CurrentStep)
            {
                case FirstRunStepKind.Tmdb:
                    await TestTmdbAsync();
                    break;
                case FirstRunStepKind.Qbittorrent:
                    await TestQbittorrentAsync();
                    break;
                case FirstRunStepKind.Jellyfin:
                    await TestJellyfinAsync();
                    break;
                case FirstRunStepKind.Warp:
                    await TestWarpAsync();
                    break;
                case FirstRunStepKind.Gemini:
                    await TestGeminiAsync();
                    break;
                case FirstRunStepKind.Drive:
                    await ConnectDriveAsync();
                    break;
                default:
                    StatusMessage = string.Empty;
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = $"Test failed: {ex.Message}";
            _logger.Error("First-run Test failed.", ex, LogTarget.All);
        }
        finally
        {
            IsBusy = false;
            NotifyNavigation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseStateFolder))]
    private void BrowseStateFolder()
    {
        var selected = BrowseFolder("Select state folder", StateFolder);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            StateFolder = selected;
        }
    }

    [RelayCommand]
    private void BrowseRestoreFolder()
    {
        var selected = BrowseFolder("Select copied state folder", RestorePath);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            RestorePath = selected;
        }
    }

    [RelayCommand]
    private void BrowseRestoreZip()
    {
        var selected = BrowseFile("Select backup zip", "Zip archives (*.zip)|*.zip|All files (*.*)|*.*", "backup.zip");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            RestorePath = selected;
        }
    }

    [RelayCommand]
    private void SelectRail(FirstRunRailRow? row)
    {
        if (row is null || !row.CanJump || row.PipelineIndex < 0)
        {
            return;
        }

        StepIndex = row.PipelineIndex;
        ApplyCurrentStep();
    }

    [RelayCommand(CanExecute = nameof(CanSearchWarmup))]
    private async Task SearchWarmupAsync()
    {
        var query = WarmupQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        ApplyWorkingSettings();
        _warmupSearchCts?.Cancel();
        _warmupSearchCts = new CancellationTokenSource();
        var token = _warmupSearchCts.Token;
        IsWarmupSearching = true;
        StatusIsError = false;
        StatusMessage = $"Searching TMDB for '{query}'…";
        WarmupResults.Clear();
        SelectedWarmupResult = null;
        try
        {
            var showsTask = _trackedShowService.SearchShowsAsync(query, token);
            var moviesTask = _trackedMovieService.SearchMoviesAsync(query, token);
            await Task.WhenAll(showsTask, moviesTask);
            if (token.IsCancellationRequested)
            {
                return;
            }

            var existingShows = _trackedShowService.GetShows().Select(show => show.TmdbId).ToHashSet();
            var existingMovies = _trackedMovieService.GetMovies().Select(movie => movie.TmdbId).ToHashSet();
            foreach (var show in showsTask.Result)
            {
                WarmupResults.Add(new TmdbUnifiedSearchResult
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
                });
            }

            foreach (var movie in moviesTask.Result)
            {
                WarmupResults.Add(new TmdbUnifiedSearchResult
                {
                    TmdbId = movie.TmdbId,
                    Title = movie.Title,
                    MediaKind = MediaKind.Movie,
                    Year = movie.ReleaseYear,
                    Overview = movie.Overview,
                    PosterPath = movie.PosterPath,
                    IsAlreadyAdded = existingMovies.Contains(movie.TmdbId)
                });
            }

            StatusMessage = WarmupResults.Count == 0
                ? $"No TMDB results for '{query}'."
                : $"Found {WarmupResults.Count} result(s). Select one and Add.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = $"TMDB search failed: {ex.Message}";
        }
        finally
        {
            IsWarmupSearching = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddWarmup))]
    private async Task AddWarmupAsync()
    {
        if (SelectedWarmupResult is null || SelectedWarmupResult.IsAlreadyAdded)
        {
            return;
        }

        IsWarmupAdding = true;
        StatusIsError = false;
        try
        {
            if (SelectedWarmupResult.MediaKind == MediaKind.Movie)
            {
                await _trackedMovieService.AddMovieAsync(SelectedWarmupResult.ToMovieSearchResult());
            }
            else
            {
                await _trackedShowService.AddShowAsync(SelectedWarmupResult.ToShowSearchResult());
            }

            SelectedWarmupResult.IsAlreadyAdded = true;
            WarmupAddedTitles.Add(SelectedWarmupResult.Title);
            StatusMessage = $"Added '{SelectedWarmupResult.Title}'.";
            AddWarmupCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = $"Could not add '{SelectedWarmupResult.Title}': {ex.Message}";
        }
        finally
        {
            IsWarmupAdding = false;
        }
    }

    [RelayCommand]
    private void BrowseDownloadFolder()
    {
        var selected = BrowseFolder("Select download folder", DownloadFolder);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        DownloadFolder = selected;
        if (UseDownloadFolderAsSource)
        {
            SourceFolder = selected;
        }
    }

    [RelayCommand]
    private void BrowseSourceFolder()
    {
        var selected = BrowseFolder("Select source folder", SourceFolder);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SourceFolder = selected;
            UseDownloadFolderAsSource = string.Equals(DownloadFolder, selected, StringComparison.OrdinalIgnoreCase);
        }
    }

    [RelayCommand]
    private void BrowseSymlinkRoot()
    {
        var selected = BrowseFolder("Select Jellyfin symlink library root", SymlinkRoot);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SymlinkRoot = selected;
        }
    }

    [RelayCommand]
    private void BrowseWarpCli()
    {
        var selected = BrowseFile("Select warp-cli.exe", "WARP CLI (warp-cli.exe)|warp-cli.exe|Executable (*.exe)|*.exe|All files (*.*)|*.*", "warp-cli.exe");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            WarpCliPath = selected;
            _overrides.WarpCliPath = selected;
        }
    }

    [RelayCommand]
    private void BrowseDriveCredentials()
    {
        var selected = BrowseFile("Select Google OAuth client JSON", "JSON files (*.json)|*.json|All files (*.*)|*.*", "credentials.json");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            DriveCredentialsPath = selected;
        }
    }

    [RelayCommand]
    private async Task Close()
    {
        if (IsGated)
        {
            RequestQuit();
            return;
        }

        await ReleaseFirstRunWarpLeasesAsync();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public bool RequestQuit()
    {
        if (!IsGated)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        var result = AppMessageBox.Show(
            "Setup is not finished. Quit Media Manager?",
            "Host setup",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return false;
        }

        SuppressClosePrompt = true;
        _ = QuitAfterReleasingWarpAsync();
        return true;
    }

    private async Task QuitAfterReleasingWarpAsync()
    {
        await ReleaseFirstRunWarpLeasesAsync();
        QuitRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task AdvanceAsync()
    {
        if (IsLastStep)
        {
            await FinishAsync();
            return;
        }

        StepIndex++;
        ApplyCurrentStep();
    }

    private async Task FinishAsync()
    {
        if (IsContinuing)
        {
            return;
        }

        IsContinuing = true;
        StatusMessage = "Saving setup…";
        try
        {
            ApplyWorkingSettings();
            var settings = _settingsService.Current;
            settings.Startup ??= new AppStartupSettings();
            settings.Startup.SetupCompleted = true;
            settings.Startup.SetupReminderDismissed = ShouldShowReminder() ? false : true;
            _settingsService.Save();
            try
            {
                _windowsStartupService.SetEnabled(RunAtStartup);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Windows startup registration failed: {ex.Message}", LogTarget.All);
            }

            await ReleaseFirstRunWarpLeasesAsync();
            _logger.Info("First-run Finish: SetupCompleted=true. Restarting without --force-first-run.", LogTarget.All);
            SuppressClosePrompt = true;
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            IsContinuing = false;
            StatusMessage = $"Could not save setup: {ex.Message}";
            _logger.Error("First-run Finish failed.", ex, LogTarget.All);
        }
    }

    private void MarkSkipped(FirstRunStepKind step)
    {
        _skipped.Add(step);
        _satisfied.Remove(step);
        if (step == FirstRunStepKind.Jellyfin)
        {
            _jellyfinSkipped = true;
            JellyfinApiKey = null;
            var settings = _settingsService.Current;
            settings.AutoTrack ??= new AutoTrackSettings();
            settings.AutoTrack.Jellyfin ??= new JellyfinRefreshSettings();
            settings.AutoTrack.Jellyfin.ApiKey = null;
            settings.Symlink ??= new SymlinkSettings();
            settings.Symlink.Enabled = false;
            RebuildPipeline();
            if (StepIndex >= _pipeline.Count)
            {
                StepIndex = Math.Max(0, _pipeline.Count - 1);
            }
        }

        if (step == FirstRunStepKind.Symlink)
        {
            _settingsService.Current.Symlink ??= new SymlinkSettings();
            _settingsService.Current.Symlink.Enabled = false;
        }

        if (step == FirstRunStepKind.Restore)
        {
            RestorePath = string.Empty;
            RebuildPipeline();
            if (StepIndex >= _pipeline.Count)
            {
                StepIndex = Math.Max(0, _pipeline.Count - 1);
            }
        }

        if (step == FirstRunStepKind.Warmup)
        {
            WarmupResults.Clear();
            SelectedWarmupResult = null;
        }
    }

    private void RebuildPipeline()
    {
        _pipeline = [FirstRunStepKind.Start];
        _pipeline.Add(FirstRunStepKind.StateFolder);
        if (WantRestore)
        {
            _pipeline.Add(FirstRunStepKind.Restore);
        }

        _pipeline.Add(FirstRunStepKind.Folders);
        _pipeline.Add(FirstRunStepKind.Windows);
        _pipeline.Add(FirstRunStepKind.Warp);
        _pipeline.Add(FirstRunStepKind.Qbittorrent);
        _pipeline.Add(FirstRunStepKind.Tmdb);
        _pipeline.Add(FirstRunStepKind.Jellyfin);
        if (!_jellyfinSkipped)
        {
            _pipeline.Add(FirstRunStepKind.Symlink);
        }

        _pipeline.Add(FirstRunStepKind.Gemini);
        _pipeline.Add(FirstRunStepKind.Drive);
        if (IsFreshPath)
        {
            _pipeline.Add(FirstRunStepKind.Warmup);
        }

        _pipeline.Add(FirstRunStepKind.AutoTrack);
        StepCount = _pipeline.Count;
        if (StepIndex >= _pipeline.Count)
        {
            StepIndex = Math.Max(0, _pipeline.Count - 1);
        }
    }

    private bool IsFreshPath => !_restoreApplied && (!WantRestore || _skipped.Contains(FirstRunStepKind.Restore));

    private void ApplyCurrentStep()
    {
        if (_pipeline.Count == 0)
        {
            RebuildPipeline();
        }

        CurrentStep = _pipeline[Math.Clamp(StepIndex, 0, _pipeline.Count - 1)];
        PathWarning = string.Empty;
        StatusIsError = false;
        if (CurrentStep == FirstRunStepKind.StateFolder)
        {
            EvaluateStateFolder();
        }
        else if (CurrentStep == FirstRunStepKind.Folders)
        {
            RefreshFolderPreview();
            EvaluateFolders();
        }

        ApplyStepStatusMessage();
        OnPropertyChanged(nameof(StepHint));
        RefreshRail();
        NotifyNavigation();
    }

    private void LoadFromSettings()
    {
        var settings = _settingsService.Current;
        StateFolder = settings.StateFolder;
        TmdbToken = settings.TmdbReadAccessToken;
        QbittorrentUrl = string.IsNullOrWhiteSpace(settings.AutoTorrent?.QbittorrentWebUiUrl)
            ? HostSoftwareCatalog.QbittorrentDefaultWebUiUrl
            : settings.AutoTorrent.QbittorrentWebUiUrl;
        QbittorrentUsername = settings.AutoTorrent?.Username;
        QbittorrentPassword = settings.AutoTorrent?.Password;
        QbittorrentApiKey = settings.AutoTorrent?.ApiKey;
        DownloadFolder = settings.AutoTorrent?.DownloadFolders.FirstOrDefault()
                         ?? settings.AutoTorrent?.DownloadFolder
                         ?? string.Empty;
        SourceFolder = settings.SourceFolders.FirstOrDefault() ?? string.Empty;
        UseDownloadFolderAsSource = !string.IsNullOrWhiteSpace(DownloadFolder) &&
            string.Equals(DownloadFolder, SourceFolder, StringComparison.OrdinalIgnoreCase);
        JellyfinUrl = string.IsNullOrWhiteSpace(settings.AutoTrack?.Jellyfin?.BaseUrl)
            ? HostSoftwareCatalog.JellyfinDefaultBaseUrl
            : settings.AutoTrack.Jellyfin.BaseUrl;
        JellyfinApiKey = settings.AutoTrack?.Jellyfin?.ApiKey;
        SymlinkRoot = string.IsNullOrWhiteSpace(settings.Symlink?.UnifiedRoot)
            ? AppConstants.DefaultSymlinkUnifiedRoot
            : settings.Symlink.UnifiedRoot;
        WarpCliPath = string.IsNullOrWhiteSpace(settings.Warp?.ExecutablePath)
            ? AppConstants.DefaultWarpCliPath
            : settings.Warp.ExecutablePath;
        GeminiApiKey = settings.Gemini?.ApiKey;
        DriveCredentialsPath = _googleDriveClient.CredentialsFilePath;
        SelectedTheme = settings.Ui?.Theme ?? AppTheme.Light;
        RunAtStartup = settings.Startup?.RunAtStartup ?? false;
        CloseToTray = settings.Startup?.CloseToTray ?? false;
        AutoTrackEnabled = settings.AutoTrack?.Enabled ?? false;
        AutoTrackAnchorDay = settings.AutoTrack?.AnchorDayOfWeek ?? DayOfWeek.Sunday;
        AutoTrackAnchorTime = string.IsNullOrWhiteSpace(settings.AutoTrack?.AnchorTimeLocal)
            ? "21:00"
            : settings.AutoTrack.AnchorTimeLocal;
        RefreshFolderPreview();
        EvaluateStateFolder();
        EvaluateFolders();
    }

    private void ApplyWorkingSettings()
    {
        var settings = _settingsService.Current;
        if (!_launchOptions.HasStateFolderOverride && !string.IsNullOrWhiteSpace(StateFolder))
        {
            settings.StateFolder = StateFolder.Trim();
        }

        settings.TmdbReadAccessToken = string.IsNullOrWhiteSpace(TmdbToken) ? null : TmdbToken.Trim();
        settings.AutoTorrent ??= new AutoTorrentSettings();
        settings.AutoTorrent.QbittorrentWebUiUrl = string.IsNullOrWhiteSpace(QbittorrentUrl)
            ? HostSoftwareCatalog.QbittorrentDefaultWebUiUrl
            : QbittorrentUrl.Trim();
        settings.AutoTorrent.Username = string.IsNullOrWhiteSpace(QbittorrentUsername) ? null : QbittorrentUsername.Trim();
        settings.AutoTorrent.Password = string.IsNullOrWhiteSpace(QbittorrentPassword) ? null : QbittorrentPassword;
        settings.AutoTorrent.ApiKey = string.IsNullOrWhiteSpace(QbittorrentApiKey) ? null : QbittorrentApiKey.Trim();
        settings.AutoTorrent.ProcessRestart ??= new QbittorrentProcessRestartSettings();
        var downloads = string.IsNullOrWhiteSpace(DownloadFolder) ? [] : new List<string> { DownloadFolder.Trim() };
        settings.AutoTorrent.DownloadFolders = downloads;
        settings.AutoTorrent.DownloadFolder = downloads.FirstOrDefault();
        settings.SourceFolders = string.IsNullOrWhiteSpace(SourceFolder) ? [] : [SourceFolder.Trim()];
        settings.AutoTrack ??= new AutoTrackSettings();
        settings.AutoTrack.Jellyfin ??= new JellyfinRefreshSettings();
        settings.AutoTrack.Jellyfin.BaseUrl = string.IsNullOrWhiteSpace(JellyfinUrl)
            ? HostSoftwareCatalog.JellyfinDefaultBaseUrl
            : JellyfinUrl.Trim().TrimEnd('/');
        settings.AutoTrack.Jellyfin.ApiKey = string.IsNullOrWhiteSpace(JellyfinApiKey) ? null : JellyfinApiKey.Trim();
        settings.Symlink ??= new SymlinkSettings();
        if (_jellyfinSkipped || _skipped.Contains(FirstRunStepKind.Symlink))
        {
            settings.Symlink.Enabled = false;
        }

        settings.Symlink.UnifiedRoot = string.IsNullOrWhiteSpace(SymlinkRoot)
            ? AppConstants.DefaultSymlinkUnifiedRoot
            : SymlinkRoot.Trim();
        settings.Warp ??= new WarpSettings();
        settings.Warp.ExecutablePath = string.IsNullOrWhiteSpace(WarpCliPath) ? null : WarpCliPath.Trim();
        settings.Gemini ??= new GeminiSettings();
        settings.Gemini.ApiKey = string.IsNullOrWhiteSpace(GeminiApiKey) ? null : GeminiApiKey.Trim();
        settings.Backup ??= new BackupSettings();
        settings.Backup.CredentialsFilePath = string.IsNullOrWhiteSpace(DriveCredentialsPath)
            ? null
            : DriveCredentialsPath.Trim();
        settings.Ui ??= new UiSettings();
        settings.Ui.Theme = SelectedTheme;
        settings.Startup ??= new AppStartupSettings();
        settings.Startup.RunAtStartup = RunAtStartup;
        settings.Startup.CloseToTray = CloseToTray;
        settings.AutoTrack.Enabled = AutoTrackEnabled;
        settings.AutoTrack.AnchorDayOfWeek = AutoTrackAnchorDay;
        settings.AutoTrack.AnchorTimeLocal = string.IsNullOrWhiteSpace(AutoTrackAnchorTime)
            ? "21:00"
            : AutoTrackAnchorTime.Trim();
    }

    private async Task<bool> TryApplyRestoreAsync()
    {
        var source = RestorePath.Trim();
        var target = ResolveRestoreTarget();
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            StatusMessage = "Choose a backup zip or copied state folder.";
            StatusIsError = true;
            return false;
        }

        IsBusy = true;
        StatusIsError = false;
        StatusMessage = "Applying backup…";
        try
        {
            var refuseLive = _launchOptions.HasStateFolderOverride;
            var result = await Task.Run(() =>
                _backupService.ApplyLocalBackup(source, target, refuseLive)).ConfigureAwait(true);

            if (result.ReviewSettings is not null)
            {
                PrefillNonSecrets(result.ReviewSettings);
                if (result.SourceHasSecrets && ConfirmUseRestoredSecrets())
                {
                    CopySecretsToWizard(result.ReviewSettings);
                }
            }

            ApplyWorkingSettings();
            EvaluateFolders();
            _restoreApplied = true;
            _skipped.Remove(FirstRunStepKind.Restore);
            RebuildPipeline();
            StatusMessage = result.DatabaseCopied
                ? "Backup applied. Database and recipes are in the state folder."
                : "Backup applied. Settings were prefilled (no database in the backup).";
            _logger.Info(
                $"First-run local restore applied from '{source}' into '{target}'. Review='{result.ReviewSettingsPath}'.",
                LogTarget.All);
            return true;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = $"Restore failed: {ex.Message}";
            _logger.Error("First-run local restore failed.", ex, LogTarget.All);
            return false;
        }
        finally
        {
            IsBusy = false;
            NotifyNavigation();
        }
    }

    private string ResolveRestoreTarget()
    {
        if (_launchOptions.HasStateFolderOverride)
        {
            return _settingsService.Current.StateFolder;
        }

        var folder = StateFolder.Trim();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            _settingsService.Current.StateFolder = folder;
        }

        return folder;
    }

    private void PrefillNonSecrets(AppSettings restored)
    {
        var downloads = restored.AutoTorrent?.DownloadFolders;
        var download = downloads?.FirstOrDefault() ?? restored.AutoTorrent?.DownloadFolder;
        if (!string.IsNullOrWhiteSpace(download))
        {
            DownloadFolder = download;
        }

        var source = restored.SourceFolders?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(source))
        {
            SourceFolder = source;
        }

        UseDownloadFolderAsSource = !string.IsNullOrWhiteSpace(DownloadFolder) &&
            string.Equals(DownloadFolder, SourceFolder, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(restored.AutoTorrent?.QbittorrentWebUiUrl))
        {
            QbittorrentUrl = restored.AutoTorrent.QbittorrentWebUiUrl;
        }

        if (!string.IsNullOrWhiteSpace(restored.AutoTrack?.Jellyfin?.BaseUrl))
        {
            JellyfinUrl = restored.AutoTrack.Jellyfin.BaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(restored.Symlink?.UnifiedRoot))
        {
            SymlinkRoot = restored.Symlink.UnifiedRoot;
        }

        if (!string.IsNullOrWhiteSpace(restored.Warp?.ExecutablePath))
        {
            WarpCliPath = restored.Warp.ExecutablePath;
        }

        if (restored.Ui is not null)
        {
            SelectedTheme = restored.Ui.Theme;
        }

        if (restored.Startup is not null)
        {
            RunAtStartup = restored.Startup.RunAtStartup;
            CloseToTray = restored.Startup.CloseToTray;
        }
    }

    private static bool ConfirmUseRestoredSecrets()
    {
        var result = AppMessageBox.Show(
            "This backup includes saved credentials. Use them in Host setup so you can Test now?",
            "Host setup",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No);
        return result == System.Windows.MessageBoxResult.Yes;
    }

    private void CopySecretsToWizard(AppSettings restored)
    {
        if (!string.IsNullOrWhiteSpace(restored.TmdbReadAccessToken))
        {
            TmdbToken = restored.TmdbReadAccessToken;
        }

        if (!string.IsNullOrWhiteSpace(restored.Gemini?.ApiKey))
        {
            GeminiApiKey = restored.Gemini.ApiKey;
        }

        if (!string.IsNullOrWhiteSpace(restored.AutoTorrent?.Username))
        {
            QbittorrentUsername = restored.AutoTorrent.Username;
        }

        if (!string.IsNullOrWhiteSpace(restored.AutoTorrent?.Password))
        {
            QbittorrentPassword = restored.AutoTorrent.Password;
        }

        if (!string.IsNullOrWhiteSpace(restored.AutoTorrent?.ApiKey))
        {
            QbittorrentApiKey = restored.AutoTorrent.ApiKey;
        }

        if (!string.IsNullOrWhiteSpace(restored.AutoTrack?.Jellyfin?.ApiKey))
        {
            JellyfinApiKey = restored.AutoTrack.Jellyfin.ApiKey;
        }
    }

    private async Task TestTmdbAsync()
    {
        var token = TmdbToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            StatusMessage = "TMDB token is empty.";
            StatusIsError = true;
            return;
        }

        try
        {
            using var response = await SendTmdbAuthAsync(token);
            ApplyTmdbTestResponse(response);
        }
        catch (Exception ex)
        {
            if (_warpCliService.IsAvailable && _warpCliService.ActiveLeases.Count == 0)
            {
                var timeout = GetWarpConnectTimeout();
                var attempt = await _warpCliService.AcquireAsync(WarpLeaseReason.TmdbSslRecover, timeout);
                if (attempt.Connected)
                {
                    try
                    {
                        using var retry = await SendTmdbAuthAsync(token);
                        ApplyTmdbTestResponse(retry);
                        return;
                    }
                    catch (Exception retryEx)
                    {
                        StatusIsError = true;
                        StatusMessage =
                            $"Cannot reach TMDB ({retryEx.Message}). If this network needs Cloudflare WARP, go Back to WARP or Skip TMDB and set it up in Settings.";
                        _logger.Error("First-run TMDB token test failed after WARP recover.", retryEx, LogTarget.All);
                        return;
                    }
                }
            }

            StatusIsError = true;
            StatusMessage =
                $"Cannot reach TMDB ({ex.Message}). If this network needs Cloudflare WARP, go Back to WARP or Skip TMDB and set it up in Settings.";
            _logger.Error("First-run TMDB token test failed before token validation.", ex, LogTarget.All);
        }
    }

    private async Task<HttpResponseMessage> SendTmdbAuthAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.themoviedb.org/3/authentication");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await _httpClient.SendAsync(request);
    }

    private void ApplyTmdbTestResponse(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            StatusMessage = "TMDB token was rejected. Check the read access token.";
            StatusIsError = true;
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            StatusMessage = $"TMDB responded with HTTP {(int)response.StatusCode} {response.ReasonPhrase}.";
            StatusIsError = true;
            return;
        }

        _satisfied.Add(FirstRunStepKind.Tmdb);
        StatusMessage = "TMDB token is valid.";
        _logger.Info("First-run TMDB token test succeeded.", LogTarget.All);
    }

    private async Task TestQbittorrentAsync()
    {
        try
        {
            var version = await _qbittorrentClient.TestConnectionAsync();
            _satisfied.Add(FirstRunStepKind.Qbittorrent);
            StatusMessage = string.IsNullOrWhiteSpace(version)
                ? "Connected to qBittorrent."
                : $"Connected to qBittorrent {version}.";
            try
            {
                var plugins = await _searchPlugins.GetPluginsAsync(forceRefresh: true);
                if (plugins.Count == 0)
                {
                    StatusMessage += " No search plugins found — recipes need a search source. You can add plugins in qBittorrent.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage += $" Could not list search plugins ({ex.Message}).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            StatusIsError = true;
            _logger.Error($"qBittorrent connection test failed: {ex.Message}", ex, LogTarget.All);
        }
    }

    private async Task TestJellyfinAsync()
    {
        try
        {
            var summary = await _jellyfinClient.TestConnectionAsync();
            _satisfied.Add(FirstRunStepKind.Jellyfin);
            StatusMessage = string.IsNullOrWhiteSpace(summary) ? "Jellyfin connection succeeded." : summary;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            StatusIsError = true;
            _logger.Error("Jellyfin connection test failed.", ex, LogTarget.All);
        }
    }

    private async Task TestWarpAsync()
    {
        ApplyWorkingSettings();
        if (!_warpCliService.IsAvailable)
        {
            StatusMessage = $"warp-cli.exe not found at {_warpCliService.ResolvedExecutablePath}.";
            StatusIsError = true;
            return;
        }

        StatusMessage = "Connecting WARP for Host setup…";
        var attempt = await _warpCliService.AcquireAsync(WarpLeaseReason.FirstRun, GetWarpConnectTimeout());
        if (!attempt.Connected)
        {
            StatusMessage = "WARP connect failed or timed out. TMDB Test may still fail on this network.";
            StatusIsError = true;
            return;
        }

        _satisfied.Add(FirstRunStepKind.Warp);
        StatusMessage = attempt.StartedByThisAcquire
            ? "WARP connected. It stays on until you Finish or close Host setup."
            : "WARP is already connected. Host setup will keep the lease until Finish.";
        _logger.Info("First-run WARP lease acquired; held until Host setup Finish.", LogTarget.All);
    }

    private TimeSpan GetWarpConnectTimeout()
    {
        var seconds = _settingsService.Current.Warp?.ConnectTimeoutSeconds ?? 30;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 120));
    }

    private async Task ReleaseFirstRunWarpLeasesAsync()
    {
        try
        {
            await _warpCliService.ReleaseAsync(WarpLeaseReason.FirstRun);
            await _warpCliService.ReleaseAsync(WarpLeaseReason.TmdbSslRecover);
        }
        catch (Exception ex)
        {
            _logger.Warning($"First-run WARP lease release failed: {ex.Message}", LogTarget.All);
        }
    }

    public Task ReleaseHeldWarpLeasesAsync() => ReleaseFirstRunWarpLeasesAsync();

    private async Task TestGeminiAsync()
    {
        if (string.IsNullOrWhiteSpace(GeminiApiKey))
        {
            StatusMessage = "Gemini API key is empty.";
            StatusIsError = true;
            return;
        }

        try
        {
            var model = await _geminiApiClient.TestConnectionAsync();
            _satisfied.Add(FirstRunStepKind.Gemini);
            StatusMessage = $"Gemini connection succeeded (model: {model}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gemini connection failed: {ex.Message}";
            StatusIsError = true;
            _logger.Error("Gemini connection test failed.", ex, LogTarget.All);
        }
    }

    private async Task ConnectDriveAsync()
    {
        ApplyWorkingSettings();
        _settingsService.Save();
        if (!File.Exists(DriveCredentialsPath))
        {
            StatusMessage = "Google OAuth client JSON not found. Browse to select the file first.";
            StatusIsError = true;
            return;
        }

        try
        {
            StatusMessage = "Opening browser to connect Google Drive…";
            var result = await _googleDriveClient.ConnectAsync();
            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                _satisfied.Add(FirstRunStepKind.Drive);
            }
            else
            {
                StatusIsError = true;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Google Drive connection failed: {ex.Message}";
            StatusIsError = true;
            _logger.Error("Google Drive connection failed.", ex, LogTarget.All);
        }
    }

    private void EvaluateStateFolder()
    {
        if (_launchOptions.HasStateFolderOverride)
        {
            _satisfied.Add(FirstRunStepKind.StateFolder);
            PathWarning = string.Empty;
            return;
        }

        var error = ValidateWritableFolder(StateFolder, rejectExeDirectory: true);
        PathWarning = error ?? NtfsWarning(StateFolder) ?? string.Empty;
        if (error is null && !string.IsNullOrWhiteSpace(StateFolder))
        {
            _satisfied.Add(FirstRunStepKind.StateFolder);
        }
        else
        {
            _satisfied.Remove(FirstRunStepKind.StateFolder);
        }
    }

    private void EvaluateFolders()
    {
        var downloadError = ValidateWritableFolder(DownloadFolder, rejectExeDirectory: false);
        var sourceError = ValidateWritableFolder(SourceFolder, rejectExeDirectory: false);
        var warn = downloadError ?? sourceError ?? NtfsWarning(DownloadFolder) ?? NtfsWarning(SourceFolder);
        PathWarning = warn ?? string.Empty;
        if (downloadError is null && sourceError is null &&
            !string.IsNullOrWhiteSpace(DownloadFolder) &&
            !string.IsNullOrWhiteSpace(SourceFolder))
        {
            _satisfied.Add(FirstRunStepKind.Folders);
        }
        else
        {
            _satisfied.Remove(FirstRunStepKind.Folders);
        }
    }

    private void RefreshFolderPreview()
    {
        if (string.IsNullOrWhiteSpace(SourceFolder) || !Directory.Exists(SourceFolder))
        {
            LibraryPreview = string.Empty;
            return;
        }

        var roots = _libraryPathResolver.GetPreviewRoots([SourceFolder]);
        LibraryPreview = roots.Count == 0
            ? string.Empty
            : "Library will live at " + string.Join(", ", roots);
    }

    private bool IsMustSatisfied(FirstRunStepKind step) => _satisfied.Contains(step);

    private bool IsFilledOptional(FirstRunStepKind step) => step switch
    {
        FirstRunStepKind.Jellyfin => !string.IsNullOrWhiteSpace(JellyfinApiKey),
        FirstRunStepKind.Qbittorrent => _satisfied.Contains(FirstRunStepKind.Qbittorrent),
        FirstRunStepKind.Tmdb => _satisfied.Contains(FirstRunStepKind.Tmdb),
        FirstRunStepKind.Warp => _warpCliService.IsAvailable,
        FirstRunStepKind.Gemini => !string.IsNullOrWhiteSpace(GeminiApiKey),
        FirstRunStepKind.Drive => _googleDriveClient.HasStoredCredential,
        FirstRunStepKind.Warmup => WarmupAddedTitles.Count > 0,
        FirstRunStepKind.AutoTrack => AutoTrackEnabled,
        _ => true
    };

    private bool ShouldShowReminder() =>
        _skipped.Contains(FirstRunStepKind.Tmdb) ||
        _skipped.Contains(FirstRunStepKind.Qbittorrent) ||
        _skipped.Contains(FirstRunStepKind.Jellyfin) ||
        _jellyfinSkipped ||
        _skipped.Contains(FirstRunStepKind.Warp) ||
        _skipped.Contains(FirstRunStepKind.Gemini) ||
        _skipped.Contains(FirstRunStepKind.Drive) ||
        _skipped.Contains(FirstRunStepKind.AutoTrack) ||
        !AutoTrackEnabled ||
        string.IsNullOrWhiteSpace(TmdbToken) ||
        !_satisfied.Contains(FirstRunStepKind.Tmdb) ||
        !_satisfied.Contains(FirstRunStepKind.Qbittorrent) ||
        string.IsNullOrWhiteSpace(JellyfinApiKey) ||
        string.IsNullOrWhiteSpace(GeminiApiKey) ||
        !_googleDriveClient.HasStoredCredential;

    private HostScanRow? GetScan(HostScanComponentId id) =>
        _scanById.TryGetValue(id, out var row) ? row : null;

    private void NotifyNavigation()
    {
        BackCommand.NotifyCanExecuteChanged();
        SkipCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        TestCommand.NotifyCanExecuteChanged();
        BrowseStateFolderCommand.NotifyCanExecuteChanged();
        SearchWarmupCommand.NotifyCanExecuteChanged();
        AddWarmupCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PrimaryButtonText));
        OnPropertyChanged(nameof(ShowSkip));
        OnPropertyChanged(nameof(ShowTest));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(StepProgressText));
        OnPropertyChanged(nameof(StepSectionTitle));
        OnPropertyChanged(nameof(CanBrowseStateFolder));
    }

    private void ApplyStepStatusMessage()
    {
        StatusIsError = false;
        StatusMessage = CurrentStep switch
        {
            FirstRunStepKind.Start => "Choose how this install should begin.",
            FirstRunStepKind.Restore => "Browse a local zip or copied state folder, then Next to copy the database into the state folder.",
            FirstRunStepKind.StateFolder when IsStateFolderPinned => "This launch is pinned by --state-folder.",
            FirstRunStepKind.StateFolder => "Pick a writable folder that is not under the app directory.",
            FirstRunStepKind.Folders => "Choose download and source folders, then Next.",
            FirstRunStepKind.Windows => "Theme and Windows startup. Skip if you are fine with the defaults.",
            FirstRunStepKind.Qbittorrent => "Enter WebUI login, then Test. Skip if qBittorrent is not ready.",
            FirstRunStepKind.Tmdb => "Paste the TMDB read access token, then Test. Test needs a reachable TMDB API (WARP on some networks). Skip is OK.",
            FirstRunStepKind.Jellyfin => "Enter the server URL and API key, then Test. Skip if you are not using Jellyfin yet.",
            FirstRunStepKind.Symlink => "Choose the unified library folder Jellyfin should see.",
            FirstRunStepKind.Warp => "Browse to warp-cli.exe if the default path is wrong, then Test. Test connects WARP and keeps it on until Finish.",
            FirstRunStepKind.Gemini => "Paste an API key, then Test. Skip if you do not need specials mapping.",
            FirstRunStepKind.Drive => "Browse the OAuth client JSON, then Test. Skip if you will back up later.",
            FirstRunStepKind.Warmup => "Search TMDB and add a few titles. Skip if TMDB was skipped or you want an empty library.",
            FirstRunStepKind.AutoTrack => "Leave this off until hunt is ready. Skip keeps it off.",
            _ => string.Empty
        };
    }

    private void RefreshRail()
    {
        RailRows.Clear();
        string? lastGroup = null;
        for (var i = 0; i < _pipeline.Count; i++)
        {
            var step = _pipeline[i];
            var group = GroupFor(step);
            if (!string.Equals(group, lastGroup, StringComparison.Ordinal))
            {
                RailRows.Add(new FirstRunRailRow
                {
                    IsGroupHeader = true,
                    Title = group
                });
                lastGroup = group;
            }

            RailRows.Add(new FirstRunRailRow
            {
                Title = TitleFor(step),
                Step = step,
                PipelineIndex = i,
                IsCurrent = i == StepIndex,
                IsCompleted = i < StepIndex,
                CanJump = i < StepIndex
            });
        }
    }

    private static string GroupFor(FirstRunStepKind step) => step switch
    {
        FirstRunStepKind.Start => "Start",
        FirstRunStepKind.StateFolder or FirstRunStepKind.Restore or FirstRunStepKind.Folders or FirstRunStepKind.Windows => "This PC",
        FirstRunStepKind.Warmup or FirstRunStepKind.AutoTrack => "Library",
        _ => "Apps"
    };

    private static string TitleFor(FirstRunStepKind step) => step switch
    {
        FirstRunStepKind.Start => "How you start",
        FirstRunStepKind.Restore => "Backup location",
        FirstRunStepKind.StateFolder => "State folder",
        FirstRunStepKind.Folders => "Folders",
        FirstRunStepKind.Windows => "Windows",
        FirstRunStepKind.Qbittorrent => "qBittorrent",
        FirstRunStepKind.Tmdb => "TMDB",
        FirstRunStepKind.Jellyfin => "Jellyfin",
        FirstRunStepKind.Symlink => "Jellyfin library",
        FirstRunStepKind.Warp => "WARP",
        FirstRunStepKind.Gemini => "Gemini",
        FirstRunStepKind.Drive => "Google Drive",
        FirstRunStepKind.Warmup => "Add titles",
        FirstRunStepKind.AutoTrack => "Auto-Track",
        _ => "Host setup"
    };

    private static string? ValidateWritableFolder(string? path, bool rejectExeDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Choose a folder.";
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        if (rejectExeDirectory)
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrWhiteSpace(exeDir) &&
                full.StartsWith(Path.GetFullPath(exeDir), StringComparison.OrdinalIgnoreCase))
            {
                return "Do not put the state folder under the app directory.";
            }
        }

        try
        {
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, ".mm-write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            return $"Folder is not writable: {ex.Message}";
        }

        return null;
    }

    private static string? NtfsWarning(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return null;
            }

            if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                return $"This drive is {drive.DriveFormat}. NTFS is preferred for the library.";
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? BrowseFolder(string description, string? initial)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(initial) ? initial : string.Empty
        };
        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static string? BrowseFile(string title, string filter, string fileName)
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = fileName,
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.FileName : null;
    }
}
