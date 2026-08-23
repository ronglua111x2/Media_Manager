using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.Services.Backup;
using media_management_app.Services.Gemini;
using media_management_app.Services.Symlink;
using WinForms = System.Windows.Forms;

namespace media_management_app.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _databaseService;
    private readonly ILibraryPathResolver _libraryPathResolver;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IWarpCliService _warpCliService;
    private readonly IWindowsStartupService _windowsStartupService;
    private readonly ITrayIconService _trayIconService;
    private readonly IWindowsNotificationService _windowsNotificationService;
    private readonly IThemeService _themeService;
    private readonly ISymlinkCoordinatorService _symlinkCoordinatorService;
    private readonly ISymlinkService _symlinkService;
    private readonly IJellyfinLibraryRefreshService _jellyfinLibraryRefreshService;
    private readonly HttpClient _httpClient;
    private readonly IGeminiApiClient _geminiApiClient;
    private readonly IGeminiModelCatalogService _geminiModelCatalog;
    private readonly GeminiQuotaTracker _geminiQuotaTracker;
    private readonly IGoogleDriveClient _googleDriveClient;
    private readonly IBackupService _backupService;
    private readonly IAppLogger _logger;
    private bool _isLoadingSettings;

    [ObservableProperty]
    private string stateFolder = string.Empty;

    [ObservableProperty]
    private string defaultLibraryFolderName = string.Empty;

    [ObservableProperty]
    private bool symlinkEnabled = true;

    [ObservableProperty]
    private string symlinkUnifiedRoot = AppConstants.DefaultSymlinkUnifiedRoot;

    [ObservableProperty]
    private bool symlinkSyncOnStartup = true;

    [ObservableProperty]
    private bool isRunningAsAdministrator;

    [ObservableProperty]
    private string administratorStatusLabel = "Unknown";

    [ObservableProperty]
    private string symlinkPreviewShowsPath = string.Empty;

    [ObservableProperty]
    private string symlinkPreviewMoviesPath = string.Empty;

    [ObservableProperty]
    private string? tmdbReadAccessToken;

    [ObservableProperty]
    private bool isTmdbTokenVisible;

    [ObservableProperty]
    private string tmdbReadAccessTokenMasked = string.Empty;

    [ObservableProperty]
    private bool geminiEnabled;

    [ObservableProperty]
    private string? geminiApiKey;

    [ObservableProperty]
    private bool isGeminiApiKeyVisible;

    [ObservableProperty]
    private string geminiApiKeyMasked = string.Empty;

    public ObservableCollection<GeminiModelOptionViewModel> GeminiModelOptions { get; } = [];

    [ObservableProperty]
    private GeminiModelOptionViewModel? selectedGeminiModel;

    [ObservableProperty]
    private string geminiModel = AppConstants.DefaultGeminiModel;

    public ObservableCollection<GeminiModelOptionViewModel> GeminiFallbackModels { get; } = [];

    public ObservableCollection<GeminiModelOptionViewModel> GeminiFallbackPickerOptions { get; } = [];

    [ObservableProperty]
    private GeminiModelOptionViewModel? selectedGeminiFallback;

    [ObservableProperty]
    private GeminiModelOptionViewModel? selectedGeminiFallbackToAdd;

    [ObservableProperty]
    private string geminiModelChainSummary = string.Empty;

    [ObservableProperty]
    private string geminiModelsFilePath = string.Empty;

    [ObservableProperty]
    private string geminiDailyUsageLabel = "0/1500 today";

    [ObservableProperty]
    private string qbittorrentWebUiUrl = "http://localhost:8080";

    [ObservableProperty]
    private string? qbittorrentUsername;

    [ObservableProperty]
    private string? qbittorrentPassword;

    [ObservableProperty]
    private bool qbittorrentConfirmCloseViewer = true;

    [ObservableProperty]
    private bool qbittorrentAutoCloseViewerOnBackground = true;

    [ObservableProperty]
    private bool qbittorrentProcessRestartEnabled;

    [ObservableProperty]
    private string qbittorrentProcessRestartExecutablePath = QbittorrentProcessRestartSettings.DefaultExecutablePath;

    [ObservableProperty]
    private int qbittorrentProcessRestartGracefulShutdownSeconds =
        QbittorrentProcessRestartSettings.DefaultGracefulShutdownSeconds;

    [ObservableProperty]
    private int qbittorrentProcessRestartCooldownMinutes =
        QbittorrentProcessRestartSettings.DefaultCooldownMinutes;

    [ObservableProperty]
    private int qbittorrentProcessRestartMaxRestartsPerHour =
        QbittorrentProcessRestartSettings.DefaultMaxRestartsPerHour;

    [ObservableProperty]
    private int qbittorrentProcessRestartWebUiReadyTimeoutSeconds =
        QbittorrentProcessRestartSettings.DefaultWebUiReadyTimeoutSeconds;

    [ObservableProperty]
    private string autoTorrentDownloadFolder = string.Empty;

    [ObservableProperty]
    private string? selectedAutoTorrentDownloadFolder;

    [ObservableProperty]
    private string autoTorrentTvShowCategoryName = AppConstants.QbittorrentTvShowCategory;

    [ObservableProperty]
    private string autoTorrentMovieCategoryName = AppConstants.QbittorrentMovieCategory;

    [ObservableProperty]
    private bool autoLinkCompletedDownloads;

    [ObservableProperty]
    private int logMaxLinesPerFile = AppConstants.MaxLogLinesPerFile;

    [ObservableProperty]
    private int logCleanupRetentionDays = AppConstants.DefaultLogCleanupRetentionDays;

    [ObservableProperty]
    private bool logAutoCloseConsoleOnBackground = true;

    [ObservableProperty]
    private bool runAtStartup;

    [ObservableProperty]
    private bool startMinimized;

    [ObservableProperty]
    private bool closeToTray;

    [ObservableProperty]
    private bool autoTrackEnabled = true;

    [ObservableProperty]
    private bool autoTrackEnforceGlobalWeeklySchedule = true;

    [ObservableProperty]
    private DayOfWeek autoTrackAnchorDay = DayOfWeek.Sunday;

    public IReadOnlyList<DayOfWeek> AnchorDayOptions => Enum.GetValues<DayOfWeek>();

    [ObservableProperty]
    private string autoTrackAnchorTimeLocal = "21:00";

    [ObservableProperty]
    private DateTime? autoTrackAnchorTime;

    [ObservableProperty]
    private int autoTrackTmdbCheckIntervalMinutes = 30;

    [ObservableProperty]
    private int autoTrackTorrentHuntIntervalMinutes = 60;

    [ObservableProperty]
    private int autoTrackHuntMinHoursAfterAirDate = 4;

    [ObservableProperty]
    private int autoTrackReconcileIntervalMinutes = 10;

    [ObservableProperty]
    private int autoTrackMaxTmdbRefreshesPerDay = 20;

    [ObservableProperty]
    private string autoTrackMinQuality = "1080p";

    [ObservableProperty]
    private int autoTrackMinSeeders;

    [ObservableProperty]
    private int autoTrackMinFileSizeMb;

    [ObservableProperty]
    private int autoTrackMaxFileSizeMb;

    [ObservableProperty]
    private string autoTrackAllowedQualities = string.Empty;

    [ObservableProperty]
    private int autoTrackMaxShowsPerHuntCycle = 3;

    [ObservableProperty]
    private int autoTrackMaxEpisodesPerShowPerHuntCycle = 5;

    [ObservableProperty]
    private int autoTrackMaxParallelWorkersPerShow = 1;

    [ObservableProperty]
    private bool autoTrackForceParallelEpisodeSearch = true;

    [ObservableProperty]
    private bool autoTrackJellyfinRefreshEnabled;

    [ObservableProperty]
    private string autoTrackJellyfinBaseUrl = "http://127.0.0.1:8096";

    [ObservableProperty]
    private string? autoTrackJellyfinApiKey;

    [ObservableProperty]
    private bool isJellyfinApiKeyVisible;

    [ObservableProperty]
    private string autoTrackJellyfinApiKeyMasked = string.Empty;

    [ObservableProperty]
    private int autoTrackJellyfinWarpHoldSeconds = JellyfinRefreshSettings.DefaultWarpHoldSecondsAfterNotify;

    [ObservableProperty]
    private bool autoTrackJellyfinLogEarlyDisconnectEnabled;

    [ObservableProperty]
    private string? autoTrackJellyfinLogPath;

    [ObservableProperty]
    private int autoTrackJellyfinLogQuietSeconds =
        JellyfinRefreshSettings.DefaultLogQuietSecondsAfterRefresh;

    [ObservableProperty]
    private string autoTrackJellyfinLogPathTestStatus = string.Empty;

    [ObservableProperty]
    private bool autoTrackJellyfinConfirmCloseViewer = true;

    [ObservableProperty]
    private bool autoTrackJellyfinAutoCloseViewerOnBackground = true;

    [ObservableProperty]
    private bool warpEnabled = true;

    [ObservableProperty]
    private bool warpAutoRecoverOnSsl = true;

    [ObservableProperty]
    private bool warpConfirmDisconnectDuringAutoTrack = true;

    [ObservableProperty]
    private string? warpExecutablePath;

    [ObservableProperty]
    private int warpConnectTimeoutSeconds = 30;

    [ObservableProperty]
    private bool warpCliAvailable;

    [ObservableProperty]
    private string warpCliResolvedPath = string.Empty;

    [ObservableProperty]
    private string warpCliAvailabilityLabel = "Not found";

    [ObservableProperty]
    private string notificationTestTitle = "Media Manager";

    [ObservableProperty]
    private string notificationTestMessage = "This is a test notification. Click to open the app.";

    [ObservableProperty]
    private string notificationTestImagePathOrUrl = string.Empty;

    [ObservableProperty]
    private string notificationTestInlineImagePathOrUrl = string.Empty;

    public ObservableCollection<NotificationPreferenceItemViewModel> NotificationPreferences { get; } = [];

    [ObservableProperty]
    private bool backupEnabled;

    [ObservableProperty]
    private int backupDailyBackupHour = 3;

    [ObservableProperty]
    private int backupEventDebounceMinutes = 20;

    [ObservableProperty]
    private int backupDbThrottleHours = 1;

    [ObservableProperty]
    private int backupHistoryRetentionCount = 20;

    [ObservableProperty]
    private string backupMachineId = string.Empty;

    [ObservableProperty]
    private bool backupIsConnected;

    [ObservableProperty]
    private string backupConnectionStatusLabel = "Not connected";

    [ObservableProperty]
    private string backupCredentialsFilePath = string.Empty;

    [ObservableProperty]
    private string backupLastRunLabel = "Never backed up.";

    public ObservableCollection<BackupHistoryItemViewModel> BackupHistory { get; } = [];

    [ObservableProperty]
    private BackupHistoryItemViewModel? selectedBackupHistoryItem;

    [ObservableProperty]
    private string? selectedSourceFolder;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private SettingsSection selectedSettingsSection = SettingsSection.System;

    [ObservableProperty]
    private AppTheme selectedTheme = AppTheme.Light;

    public Array ThemeOptions => Enum.GetValues(typeof(AppTheme));

    public SettingsViewModel(
        ISettingsService settingsService,
        IDatabaseService databaseService,
        ILibraryPathResolver libraryPathResolver,
        IQbittorrentClient qbittorrentClient,
        IWarpCliService warpCliService,
        IWindowsStartupService windowsStartupService,
        ITrayIconService trayIconService,
        IWindowsNotificationService windowsNotificationService,
        IThemeService themeService,
        ISymlinkCoordinatorService symlinkCoordinatorService,
        ISymlinkService symlinkService,
        IJellyfinLibraryRefreshService jellyfinLibraryRefreshService,
        HttpClient httpClient,
        IGeminiApiClient geminiApiClient,
        IGeminiModelCatalogService geminiModelCatalog,
        GeminiQuotaTracker geminiQuotaTracker,
        IGoogleDriveClient googleDriveClient,
        IBackupService backupService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;
        _libraryPathResolver = libraryPathResolver;
        _qbittorrentClient = qbittorrentClient;
        _warpCliService = warpCliService;
        _windowsStartupService = windowsStartupService;
        _trayIconService = trayIconService;
        _windowsNotificationService = windowsNotificationService;
        _themeService = themeService;
        _symlinkCoordinatorService = symlinkCoordinatorService;
        _symlinkService = symlinkService;
        _jellyfinLibraryRefreshService = jellyfinLibraryRefreshService;
        _httpClient = httpClient;
        _geminiApiClient = geminiApiClient;
        _geminiModelCatalog = geminiModelCatalog;
        _geminiQuotaTracker = geminiQuotaTracker;
        _googleDriveClient = googleDriveClient;
        _backupService = backupService;
        _logger = logger;
        SourceFolders = [];
        AutoTorrentDownloadFolders = [];
        LibraryRootPreview = [];
        LoadFromSettings();
    }

    public override void OnNavigatedTo()
    {
        // Sprint 4: reload disk values on each visit. Dirty-tracking lands in Sprint 6.
        _settingsService.Load();
        LoadFromSettings();
    }

    public ObservableCollection<string> SourceFolders { get; }

    public ObservableCollection<string> AutoTorrentDownloadFolders { get; }

    public ObservableCollection<string> LibraryRootPreview { get; }

    [RelayCommand]
    private void BrowseStateFolder()
    {
        var selected = BrowseFolder(StateFolder, "Select state folder");
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        StateFolder = selected;
    }

    [RelayCommand]
    private void AddSourceFolder()
    {
        var selected = BrowseFolder(null, "Add source folder");
        if (string.IsNullOrWhiteSpace(selected) ||
            SourceFolders.Any(folder => string.Equals(folder, selected, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SourceFolders.Add(selected);
        SelectedSourceFolder = selected;
        RefreshLibraryRootPreview();
    }

    [RelayCommand]
    private void RemoveSelectedSourceFolder()
    {
        if (string.IsNullOrWhiteSpace(SelectedSourceFolder))
        {
            return;
        }

        SourceFolders.Remove(SelectedSourceFolder);
        SelectedSourceFolder = SourceFolders.FirstOrDefault();
        RefreshLibraryRootPreview();
    }

    [RelayCommand]
    private async Task SyncSymlinksNow()
    {
        try
        {
            var result = await _symlinkCoordinatorService.SyncNowAsync();
            StatusMessage = result.ErrorCount > 0
                ? $"{result.Summary} See logs for details."
                : result.Summary;
            _logger.Info($"Manual symlink sync finished. {result.Summary}", LogTarget.All);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Symlink sync failed: {ex.Message}";
            _logger.Error("Manual symlink sync failed.", ex, LogTarget.All);
        }
    }

    [RelayCommand]
    private void BrowseSymlinkUnifiedRoot()
    {
        var selected = BrowseFolder(SymlinkUnifiedRoot, "Select Jellyfin symlink library root");
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        SymlinkUnifiedRoot = selected;
        RefreshSymlinkPreview();
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.Current.StateFolder = StateFolder;
        _settingsService.Current.SourceFolders = SourceFolders.ToList();
        _settingsService.Current.LibraryRootMode = LibraryRootMode.AutoPerDrive;
        _settingsService.Current.DefaultLibraryFolderName = string.IsNullOrWhiteSpace(DefaultLibraryFolderName)
            ? AppConstants.DefaultLibraryFolderName
            : DefaultLibraryFolderName.Trim();
        _settingsService.Current.TmdbReadAccessToken = string.IsNullOrWhiteSpace(TmdbReadAccessToken) ? null : TmdbReadAccessToken;
        ApplyGeminiSettings();
        ApplyAutoTorrentSettings();
        ApplyWarpSettings();
        ApplyLogSettings();
        ApplyStartupSettings();
        ApplyAutoTrackSettings();
        ApplyUiSettings();
        ApplySymlinkSettings();
        ApplyNotificationSettings();
        ApplyBackupSettings();
        _settingsService.Save();
        try
        {
            _windowsStartupService.SetEnabled(RunAtStartup);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Settings saved, but Windows startup registration failed: {ex.Message}";
            _logger.Warning($"Windows startup registration failed: {ex.Message}", LogTarget.All);
            return;
        }

        EnsureTrayInitialized();
        _themeService.Apply(SelectedTheme);
        _databaseService.Initialize(_settingsService.Current.StateFolder);
        RefreshLibraryRootPreview();
        StatusMessage = $"Saved settings to {_settingsService.SettingsFilePath}";
        _logger.Info($"Saved settings to {_settingsService.SettingsFilePath}", LogTarget.All);
    }

    [RelayCommand]
    private void SendTestNotification()
    {
        var title = string.IsNullOrWhiteSpace(NotificationTestTitle) ? "Media Manager" : NotificationTestTitle.Trim();
        var message = string.IsNullOrWhiteSpace(NotificationTestMessage) ? "Test notification." : NotificationTestMessage.Trim();
        var sent = _windowsNotificationService.TryShow(new WindowsNotificationRequest
        {
            Title = title,
            Message = message,
            Kind = NotificationKind.Test,
            Tag = null,
            HeroImagePathOrUrl = string.IsNullOrWhiteSpace(NotificationTestImagePathOrUrl)
                ? null
                : NotificationTestImagePathOrUrl.Trim(),
            AppLogoOverridePathOrUrl = string.IsNullOrWhiteSpace(NotificationTestInlineImagePathOrUrl)
                ? null
                : NotificationTestInlineImagePathOrUrl.Trim()
        });
        StatusMessage = sent
            ? "Test notification sent. Check Windows Action Center."
            : "Failed to send notification. Check Windows notification settings or Focus Assist.";
    }

    [RelayCommand]
    private void BrowseNotificationTestImage()
    {
        var selected = BrowseNotificationImageFile(
            NotificationTestImagePathOrUrl,
            "Select hero / cover image");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            NotificationTestImagePathOrUrl = selected;
        }
    }

    [RelayCommand]
    private void BrowseNotificationTestInlineImage()
    {
        var selected = BrowseNotificationImageFile(
            NotificationTestInlineImagePathOrUrl,
            "Select inline icon (beside text)");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            NotificationTestInlineImagePathOrUrl = selected;
        }
    }

    [RelayCommand]
    private void UseWarpLogoForTestInline()
    {
        if (!TrySetBrandLogoForTestInline("warp-logo.png", out var path))
        {
            StatusMessage = "WARP logo not found. Rebuild so Assets/notifications/warp-logo.png is copied to output.";
            return;
        }

        NotificationTestInlineImagePathOrUrl = path;
        StatusMessage = $"Inline test icon set to WARP logo: {path}";
    }

    [RelayCommand]
    private void UseJellyfinLogoForTestInline()
    {
        if (!TrySetBrandLogoForTestInline("jellyfin-logo.png", out var path))
        {
            StatusMessage = "Jellyfin logo not found. Rebuild so Assets/notifications/jellyfin-logo.png is copied to output.";
            return;
        }

        NotificationTestInlineImagePathOrUrl = path;
        StatusMessage = $"Inline test icon set to Jellyfin logo: {path}";
    }

    private static string? BrowseNotificationImageFile(string? currentPath, string title)
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = title,
            Filter = "Image files (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            dialog.FileName = Path.GetFileName(currentPath);
        }

        return dialog.ShowDialog() == WinForms.DialogResult.OK
            ? dialog.FileName
            : null;
    }

    private static bool TrySetBrandLogoForTestInline(string fileName, out string absolutePath)
    {
        absolutePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Assets", "notifications", fileName));
        return File.Exists(absolutePath);
    }

    [RelayCommand]
    private async Task TestQbittorrentConnection()
    {
        ApplyAutoTorrentSettings();
        try
        {
            StatusMessage = "Testing qBittorrent connection...";
            var version = await _qbittorrentClient.TestConnectionAsync();
            StatusMessage = $"Connected to qBittorrent {version}. Save settings to persist this connection.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.Error($"qBittorrent connection test failed: {ex.Message}", ex, LogTarget.All);
        }
    }

    [RelayCommand]
    private async Task RefreshGeminiModels()
    {
        ApplyGeminiSettings();
        if (string.IsNullOrWhiteSpace(GeminiApiKey))
        {
            StatusMessage = "Gemini API key is required to refresh models from API.";
            return;
        }

        try
        {
            StatusMessage = "Refreshing Gemini model catalog from API...";
            var result = await _geminiModelCatalog.RefreshFromApiAsync();
            ReloadGeminiModelOptions();
            ReloadGeminiFallbackModels();
            GeminiModel = NormalizeGeminiModel(GeminiModel);
            StatusMessage = $"Model catalog refreshed: {result.Summary}";
            _logger.Info($"Gemini model catalog refreshed: {result.Summary}", LogTarget.All);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Model catalog refresh failed: {ex.Message}";
            _logger.Error($"Gemini model catalog refresh failed: {ex.Message}", ex, LogTarget.All);
        }
    }

    [RelayCommand]
    private void OpenGeminiModelsFile()
    {
        _geminiModelCatalog.ReloadFromDisk();
        var path = _geminiModelCatalog.CatalogFilePath;
        if (!File.Exists(path))
        {
            StatusMessage = $"Model catalog file not found: {path}";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            StatusMessage = $"Opened {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open model catalog file: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestGemini()
    {
        ApplyGeminiSettings();
        if (string.IsNullOrWhiteSpace(GeminiApiKey))
        {
            StatusMessage = "Gemini API key is empty.";
            return;
        }

        try
        {
            StatusMessage = "Testing Gemini connection...";
            var modelUsed = await _geminiApiClient.TestConnectionAsync();
            RefreshGeminiUsageLabel();
            StatusMessage = $"Gemini connection succeeded (model: {modelUsed}).";
            _logger.Info($"Gemini connection test succeeded (model: {modelUsed}).", LogTarget.All);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            StatusMessage = "Gemini rate limited (HTTP 429). Wait a minute, then retry or pick another model.";
            _logger.Error($"Gemini connection test failed: {ex.Message}", ex, LogTarget.All);
        }
        catch (Exception ex)
        {
            StatusMessage = FormatGeminiTestError(ex);
            _logger.Error($"Gemini connection test failed: {ex.Message}", ex, LogTarget.All);
        }
    }

    [RelayCommand]
    private async Task TestTmdbToken()
    {
        var token = TmdbReadAccessToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            StatusMessage = "TMDB token is empty.";
            return;
        }

        try
        {
            StatusMessage = "Testing TMDB token...";
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.themoviedb.org/3/authentication");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request);
            StatusMessage = response.StatusCode switch
            {
                HttpStatusCode.OK => "TMDB token is valid.",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "TMDB token was rejected. Check the read access token.",
                _ => $"TMDB responded with HTTP {(int)response.StatusCode} {response.ReasonPhrase}."
            };

            if (response.IsSuccessStatusCode)
            {
                _logger.Info("TMDB token test succeeded.", LogTarget.All);
            }
            else
            {
                _logger.Warning($"TMDB token test returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.", LogTarget.All);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"TMDB connection failed before token validation: {ex.Message}";
            _logger.Error("TMDB token test failed before token validation.", ex, LogTarget.All);
        }
    }

    [RelayCommand]
    private async Task TestWarpConnection()
    {
        ApplyWarpSettings();
        RefreshWarpCliStatus();

        if (!_warpCliService.IsAvailable)
        {
            StatusMessage = $"warp-cli.exe not found at {_warpCliService.ResolvedExecutablePath}.";
            return;
        }

        try
        {
            StatusMessage = "Checking WARP status...";
            if (await _warpCliService.IsConnectedAsync())
            {
                StatusMessage = "WARP is already connected.";
                return;
            }

            StatusMessage = "Testing WARP connection...";
            var timeout = TimeSpan.FromSeconds(WarpConnectTimeoutSeconds);
            var attempt = await _warpCliService.AcquireAsync(WarpLeaseReason.SettingsTest, timeout);
            if (!attempt.Connected)
            {
                StatusMessage = $"WARP connect test failed or timed out after {WarpConnectTimeoutSeconds}s.";
                _logger.Warning(StatusMessage, LogTarget.All);
                return;
            }

            await _warpCliService.ReleaseAsync(WarpLeaseReason.SettingsTest);
            StatusMessage = "WARP connect test succeeded (disconnected after test). Save settings to persist changes.";
            _logger.Info(StatusMessage, LogTarget.All);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.Error($"WARP connection test failed: {ex.Message}", ex, LogTarget.All);
            try
            {
                await _warpCliService.ReleaseAsync(WarpLeaseReason.SettingsTest);
            }
            catch
            {
            }
        }
    }

    [RelayCommand]
    private async Task TestJellyfinConnection()
    {
        ApplyAutoTrackSettings();
        try
        {
            StatusMessage = "Testing Jellyfin connection...";
            var summary = await _jellyfinLibraryRefreshService.TestConnectionAsync();
            StatusMessage = $"Jellyfin OK: {summary}. Save settings to persist changes.";
            _logger.Info(StatusMessage, LogTarget.All);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Jellyfin connection failed: {ex.Message}";
            _logger.Error("Jellyfin connection test failed.", ex, LogTarget.All);
        }
    }

    [RelayCommand]
    private async Task FlushJellyfinRefreshQueue()
    {
        ApplyAutoTrackSettings();
        try
        {
            StatusMessage = "Flushing Jellyfin path refresh queue...";
            await _jellyfinLibraryRefreshService.FlushAsync();
            StatusMessage = "Jellyfin path refresh flush finished. See logs for details.";
            _logger.Info(StatusMessage, LogTarget.All);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Jellyfin path refresh flush failed: {ex.Message}";
            _logger.Error("Jellyfin path refresh flush failed.", ex, LogTarget.All);
        }
    }

    [RelayCommand]
    private void BrowseJellyfinLogPath()
    {
        var initial = AutoTrackJellyfinLogPath;
        try
        {
            if (!string.IsNullOrWhiteSpace(initial) && System.IO.File.Exists(initial))
            {
                initial = System.IO.Path.GetDirectoryName(initial);
            }
        }
        catch
        {
            // Ignore invalid initial path.
        }

        var selected = BrowseFolder(initial, "Select Jellyfin log folder");
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        AutoTrackJellyfinLogPath = selected;
        AutoTrackJellyfinLogPathTestStatus = string.Empty;
    }

    [RelayCommand]
    private void BrowseJellyfinLogFile()
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = "Select Jellyfin log file",
            Filter = "Jellyfin log (*.log)|*.log|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(AutoTrackJellyfinLogPath))
        {
            try
            {
                if (System.IO.File.Exists(AutoTrackJellyfinLogPath))
                {
                    dialog.InitialDirectory = System.IO.Path.GetDirectoryName(AutoTrackJellyfinLogPath);
                    dialog.FileName = System.IO.Path.GetFileName(AutoTrackJellyfinLogPath);
                }
                else if (System.IO.Directory.Exists(AutoTrackJellyfinLogPath))
                {
                    dialog.InitialDirectory = AutoTrackJellyfinLogPath;
                }
            }
            catch
            {
                // Ignore invalid initial path.
            }
        }

        if (dialog.ShowDialog() != WinForms.DialogResult.OK ||
            string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        AutoTrackJellyfinLogPath = dialog.FileName;
        AutoTrackJellyfinLogPathTestStatus = string.Empty;
    }

    [RelayCommand]
    private void TestJellyfinLogPath()
    {
        ApplyAutoTrackSettings();
        if (JellyfinLogPathValidator.TryResolveAndValidate(
                AutoTrackJellyfinLogPath,
                out var resolvedPath,
                out var errorMessage))
        {
            var fileName = System.IO.Path.GetFileName(resolvedPath);
            AutoTrackJellyfinLogPathTestStatus = $"OK — using {fileName}";
            StatusMessage = $"Jellyfin log path OK: {resolvedPath}. Save settings to persist changes.";
            _logger.Info(StatusMessage, LogTarget.All);
        }
        else
        {
            AutoTrackJellyfinLogPathTestStatus = errorMessage;
            StatusMessage = $"Jellyfin log path invalid: {errorMessage}";
            _logger.Warning(StatusMessage, LogTarget.All);
        }
    }

    [RelayCommand]
    private void BrowseAutoTorrentDownloadFolder()
    {
        var selected = BrowseFolder(AutoTorrentDownloadFolder, "Select Auto Torrent download folder");
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        AutoTorrentDownloadFolder = selected;
        if (!AutoTorrentDownloadFolders.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            AutoTorrentDownloadFolders.Add(selected);
        }
    }

    [RelayCommand]
    private void AddAutoTorrentDownloadFolder()
    {
        var selected = BrowseFolder(AutoTorrentDownloadFolder, "Add Auto Torrent download folder option");
        if (string.IsNullOrWhiteSpace(selected) ||
            AutoTorrentDownloadFolders.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        AutoTorrentDownloadFolders.Add(selected);
        SelectedAutoTorrentDownloadFolder = selected;
    }

    [RelayCommand]
    private void RemoveSelectedAutoTorrentDownloadFolder()
    {
        if (string.IsNullOrWhiteSpace(SelectedAutoTorrentDownloadFolder))
        {
            return;
        }

        AutoTorrentDownloadFolders.Remove(SelectedAutoTorrentDownloadFolder);
        SelectedAutoTorrentDownloadFolder = AutoTorrentDownloadFolders.FirstOrDefault();
    }

    [RelayCommand]
    private void ClearEpisodeSelectedCandidates()
    {
        _databaseService.ClearSelectedEpisodeCandidates();
        StatusMessage = "Cleared selected episode candidates.";
    }

    [RelayCommand]
    private void ClearSeasonPackSelectedCandidates()
    {
        _databaseService.ClearSelectedSeasonPackCandidates();
        StatusMessage = "Cleared selected season pack candidates.";
    }

    [RelayCommand]
    private void ClearMovieSelectedCandidates()
    {
        _databaseService.ClearSelectedMovieCandidates();
        StatusMessage = "Cleared selected movie candidates.";
    }

    [RelayCommand]
    private void ClearAllSelectedCandidates()
    {
        _databaseService.ClearSelectedEpisodeCandidates();
        _databaseService.ClearSelectedSeasonPackCandidates();
        _databaseService.ClearSelectedMovieCandidates();
        StatusMessage = "Cleared all selected candidates.";
    }

    partial void OnDefaultLibraryFolderNameChanged(string value)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        RefreshLibraryRootPreview();
    }

    partial void OnAutoTrackAnchorTimeChanged(DateTime? value)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        AutoTrackAnchorTimeLocal = AutoTrackWeekAnchor.FormatTimeLocal(value);
    }

    partial void OnSymlinkUnifiedRootChanged(string value)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        RefreshSymlinkPreview();
    }

    partial void OnWarpExecutablePathChanged(string? value)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        ApplyWarpSettings();
        RefreshWarpCliStatus();
    }

    partial void OnSelectedSettingsSectionChanged(SettingsSection value)
    {
        if (value == SettingsSection.Integrations)
        {
            _geminiModelCatalog.ReloadFromDisk();
            ReloadGeminiModelOptions();
            ReloadGeminiFallbackModels();
            GeminiModelsFilePath = _geminiModelCatalog.CatalogFilePath;
            RefreshWarpCliStatus();
        }
        else if (value == SettingsSection.Backup)
        {
            RefreshBackupConnectionStatus();
            _ = RefreshBackupHistory();
        }
    }

    partial void OnSelectedGeminiModelChanged(GeminiModelOptionViewModel? value)
    {
        if (_isLoadingSettings || value is null)
        {
            return;
        }

        GeminiModel = value.Id;
        ReloadGeminiFallbackPickerOptions();
        UpdateGeminiModelChainSummary();
    }

    partial void OnSelectedGeminiFallbackToAddChanged(GeminiModelOptionViewModel? value) =>
        AddGeminiFallbackCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanAddGeminiFallback))]
    private void AddGeminiFallback()
    {
        if (SelectedGeminiFallbackToAdd is null
            || GeminiFallbackModels.Count >= AppConstants.GeminiMaxFallbackModels)
        {
            return;
        }

        GeminiFallbackModels.Add(new GeminiModelOptionViewModel
        {
            Id = SelectedGeminiFallbackToAdd.Id,
            DisplayLabel = SelectedGeminiFallbackToAdd.DisplayLabel
        });
        SelectedGeminiFallbackToAdd = null;
        ReloadGeminiFallbackPickerOptions();
        UpdateGeminiModelChainSummary();
    }

    private bool CanAddGeminiFallback() =>
        SelectedGeminiFallbackToAdd is not null
        && GeminiFallbackModels.Count < AppConstants.GeminiMaxFallbackModels;

    [RelayCommand(CanExecute = nameof(CanModifySelectedGeminiFallback))]
    private void RemoveGeminiFallback()
    {
        if (SelectedGeminiFallback is null)
        {
            return;
        }

        GeminiFallbackModels.Remove(SelectedGeminiFallback);
        SelectedGeminiFallback = null;
        ReloadGeminiFallbackPickerOptions();
        UpdateGeminiModelChainSummary();
    }

    [RelayCommand(CanExecute = nameof(CanMoveGeminiFallbackUp))]
    private void MoveGeminiFallbackUp()
    {
        if (SelectedGeminiFallback is null)
        {
            return;
        }

        var index = GeminiFallbackModels.IndexOf(SelectedGeminiFallback);
        if (index <= 0)
        {
            return;
        }

        GeminiFallbackModels.Move(index, index - 1);
        UpdateGeminiModelChainSummary();
    }

    [RelayCommand(CanExecute = nameof(CanMoveGeminiFallbackDown))]
    private void MoveGeminiFallbackDown()
    {
        if (SelectedGeminiFallback is null)
        {
            return;
        }

        var index = GeminiFallbackModels.IndexOf(SelectedGeminiFallback);
        if (index < 0 || index >= GeminiFallbackModels.Count - 1)
        {
            return;
        }

        GeminiFallbackModels.Move(index, index + 1);
        UpdateGeminiModelChainSummary();
    }

    private bool CanModifySelectedGeminiFallback() => SelectedGeminiFallback is not null;

    private bool CanMoveGeminiFallbackUp()
    {
        if (SelectedGeminiFallback is null)
        {
            return false;
        }

        return GeminiFallbackModels.IndexOf(SelectedGeminiFallback) > 0;
    }

    private bool CanMoveGeminiFallbackDown()
    {
        if (SelectedGeminiFallback is null)
        {
            return false;
        }

        var index = GeminiFallbackModels.IndexOf(SelectedGeminiFallback);
        return index >= 0 && index < GeminiFallbackModels.Count - 1;
    }

    partial void OnSelectedGeminiFallbackChanged(GeminiModelOptionViewModel? value)
    {
        RemoveGeminiFallbackCommand.NotifyCanExecuteChanged();
        MoveGeminiFallbackUpCommand.NotifyCanExecuteChanged();
        MoveGeminiFallbackDownCommand.NotifyCanExecuteChanged();
    }

    private void LoadFromSettings()
    {
        _logger.Info(
            $"Loading Settings UI from service. SettingsFile='{_settingsService.SettingsFilePath}', SourceFolders={_settingsService.Current.SourceFolders.Count}, StateFolder='{_settingsService.Current.StateFolder}'",
            LogTarget.All);

        _isLoadingSettings = true;
        try
        {
            StateFolder = _settingsService.Current.StateFolder;
            DefaultLibraryFolderName = _settingsService.Current.DefaultLibraryFolderName;
            SymlinkEnabled = _settingsService.Current.Symlink?.Enabled ?? true;
            SymlinkUnifiedRoot = _settingsService.Current.Symlink?.UnifiedRoot ?? AppConstants.DefaultSymlinkUnifiedRoot;
            SymlinkSyncOnStartup = _settingsService.Current.Symlink?.SyncOnStartup ?? true;
            TmdbReadAccessToken = _settingsService.Current.TmdbReadAccessToken;
            GeminiEnabled = _settingsService.Current.Gemini?.Enabled ?? false;
            GeminiApiKey = _settingsService.Current.Gemini?.ApiKey;
            GeminiModel = NormalizeGeminiModel(_settingsService.Current.Gemini?.Model);
            ReloadGeminiModelOptions();
            ReloadGeminiFallbackModels();
            GeminiModelsFilePath = _geminiModelCatalog.CatalogFilePath;
            RefreshGeminiUsageLabel();
            QbittorrentWebUiUrl = _settingsService.Current.AutoTorrent.QbittorrentWebUiUrl;
            QbittorrentUsername = _settingsService.Current.AutoTorrent.Username;
            QbittorrentPassword = _settingsService.Current.AutoTorrent.Password;
            QbittorrentConfirmCloseViewer = _settingsService.Current.AutoTorrent.ConfirmCloseViewer;
            QbittorrentAutoCloseViewerOnBackground = _settingsService.Current.AutoTorrent.AutoCloseViewerOnBackground;
            var processRestart = _settingsService.Current.AutoTorrent.ProcessRestart
                                 ?? new QbittorrentProcessRestartSettings();
            QbittorrentProcessRestartEnabled = processRestart.Enabled;
            QbittorrentProcessRestartExecutablePath = string.IsNullOrWhiteSpace(processRestart.ExecutablePath)
                ? QbittorrentProcessRestartSettings.DefaultExecutablePath
                : processRestart.ExecutablePath;
            QbittorrentProcessRestartGracefulShutdownSeconds = processRestart.GracefulShutdownSeconds;
            QbittorrentProcessRestartCooldownMinutes = processRestart.CooldownMinutes;
            QbittorrentProcessRestartMaxRestartsPerHour = processRestart.MaxRestartsPerHour;
            QbittorrentProcessRestartWebUiReadyTimeoutSeconds = processRestart.WebUiReadyTimeoutSeconds;
            AutoTorrentDownloadFolder = _settingsService.Current.AutoTorrent.DownloadFolder ?? string.Empty;
            AutoTorrentTvShowCategoryName = _settingsService.Current.AutoTorrent.TvShowCategoryName;
            AutoTorrentMovieCategoryName = _settingsService.Current.AutoTorrent.MovieCategoryName;
            AutoLinkCompletedDownloads = _settingsService.Current.AutoTorrent.AutoLinkCompletedDownloads;
            LogMaxLinesPerFile = _settingsService.Current.Logs.MaxLinesPerFile;
            LogCleanupRetentionDays = _settingsService.Current.Logs.CleanupRetentionDays;
            LogAutoCloseConsoleOnBackground = _settingsService.Current.Logs.AutoCloseConsoleOnBackground;
            RunAtStartup = _settingsService.Current.Startup.RunAtStartup;
            StartMinimized = _settingsService.Current.Startup.StartMinimized;
            CloseToTray = _settingsService.Current.Startup.CloseToTray;
            SelectedTheme = _settingsService.Current.Ui?.Theme ?? AppTheme.Light;
            AutoTrackEnabled = _settingsService.Current.AutoTrack?.Enabled ?? true;
            var autoTrack = _settingsService.Current.AutoTrack ?? new AutoTrackSettings();
            AutoTrackEnforceGlobalWeeklySchedule = autoTrack.EnforceGlobalWeeklySchedule;
            AutoTrackAnchorDay = autoTrack.AnchorDayOfWeek;
            AutoTrackAnchorTimeLocal = autoTrack.AnchorTimeLocal;
            AutoTrackAnchorTime = AutoTrackWeekAnchor.ToTimePickerValue(autoTrack.AnchorTimeLocal);
            AutoTrackTmdbCheckIntervalMinutes = autoTrack.TmdbCheckIntervalMinutes;
            AutoTrackTorrentHuntIntervalMinutes = autoTrack.TorrentHuntIntervalMinutes;
            AutoTrackHuntMinHoursAfterAirDate = autoTrack.HuntMinHoursAfterAirDate;
            AutoTrackReconcileIntervalMinutes = autoTrack.ReconcileIntervalMinutes;
            AutoTrackMaxTmdbRefreshesPerDay = autoTrack.MaxTmdbRefreshesPerDay;
            AutoTrackMinQuality = autoTrack.Quality?.MinQuality ?? "1080p";
            AutoTrackMinSeeders = autoTrack.Quality?.MinSeeders ?? 0;
            AutoTrackMinFileSizeMb = autoTrack.Quality?.MinFileSizeMb ?? 0;
            AutoTrackMaxFileSizeMb = autoTrack.Quality?.MaxFileSizeMb ?? 0;
            AutoTrackAllowedQualities = string.Join(", ", autoTrack.Quality?.AllowedQualities ?? []);
            AutoTrackMaxShowsPerHuntCycle = autoTrack.Search?.MaxShowsPerHuntCycle ?? 3;
            AutoTrackMaxEpisodesPerShowPerHuntCycle = autoTrack.Search?.MaxEpisodesPerShowPerHuntCycle ?? 5;
            AutoTrackMaxParallelWorkersPerShow = autoTrack.Search?.MaxParallelWorkersPerShow ?? 1;
            AutoTrackForceParallelEpisodeSearch = autoTrack.Search?.ForceParallelEpisodeSearch ?? true;
            var jellyfin = autoTrack.Jellyfin ?? new JellyfinRefreshSettings();
            AutoTrackJellyfinRefreshEnabled = jellyfin.Enabled;
            AutoTrackJellyfinBaseUrl = string.IsNullOrWhiteSpace(jellyfin.BaseUrl)
                ? "http://127.0.0.1:8096"
                : jellyfin.BaseUrl;
            AutoTrackJellyfinApiKey = jellyfin.ApiKey;
            AutoTrackJellyfinWarpHoldSeconds = jellyfin.WarpHoldSecondsAfterNotify <= 0
                ? JellyfinRefreshSettings.DefaultWarpHoldSecondsAfterNotify
                : jellyfin.WarpHoldSecondsAfterNotify;
            AutoTrackJellyfinLogEarlyDisconnectEnabled = jellyfin.EnableLogEarlyDisconnect;
            AutoTrackJellyfinLogPath = jellyfin.LogPath;
            AutoTrackJellyfinLogQuietSeconds = jellyfin.LogQuietSecondsAfterRefresh <= 0
                ? JellyfinRefreshSettings.DefaultLogQuietSecondsAfterRefresh
                : jellyfin.LogQuietSecondsAfterRefresh;
            AutoTrackJellyfinConfirmCloseViewer = jellyfin.ConfirmCloseViewer;
            AutoTrackJellyfinAutoCloseViewerOnBackground = jellyfin.AutoCloseViewerOnBackground;
            AutoTrackJellyfinLogPathTestStatus = string.Empty;
            WarpEnabled = _settingsService.Current.Warp.Enabled;
            WarpAutoRecoverOnSsl = _settingsService.Current.Warp.AutoRecoverOnSsl;
            WarpConfirmDisconnectDuringAutoTrack = _settingsService.Current.Warp.ConfirmDisconnectDuringAutoTrack;
            WarpExecutablePath = _settingsService.Current.Warp.ExecutablePath;
            WarpConnectTimeoutSeconds = _settingsService.Current.Warp.ConnectTimeoutSeconds;
            var backup = _settingsService.Current.Backup ??= new BackupSettings();
            BackupEnabled = backup.Enabled;
            BackupDailyBackupHour = backup.DailyBackupHour;
            BackupEventDebounceMinutes = backup.EventDebounceMinutes;
            BackupDbThrottleHours = backup.DbThrottleHours;
            BackupHistoryRetentionCount = backup.HistoryRetentionCount;
            BackupMachineId = backup.MachineId;
            BackupCredentialsFilePath = _googleDriveClient.CredentialsFilePath;
            RefreshBackupLastRunLabel();
            LoadNotificationPreferences();
            AutoTorrentDownloadFolders.Clear();
            foreach (var folder in _settingsService.Current.AutoTorrent.DownloadFolders)
            {
                AutoTorrentDownloadFolders.Add(folder);
            }

            SelectedAutoTorrentDownloadFolder = AutoTorrentDownloadFolders.FirstOrDefault();

            SourceFolders.Clear();
            foreach (var folder in _settingsService.Current.SourceFolders)
            {
                _logger.Debug($"Adding source folder to Settings UI: {folder}", LogTarget.File | LogTarget.Console);
                SourceFolders.Add(folder);
            }

            SelectedSourceFolder = SourceFolders.FirstOrDefault();
        }
        finally
        {
            _isLoadingSettings = false;
        }

        RefreshLibraryRootPreview();
        RefreshSymlinkPreview();
        RefreshAdministratorStatus();
        RefreshWarpCliStatus();
        RefreshBackupConnectionStatus();
        _logger.Info($"Settings UI loaded. VisibleSourceFolders={SourceFolders.Count}, SelectedSourceFolder='{SelectedSourceFolder ?? "<none>"}'", LogTarget.All);
    }

    private void ApplyWarpSettings()
    {
        _settingsService.Current.Warp ??= new WarpSettings();
        _settingsService.Current.Warp.Enabled = WarpEnabled;
        _settingsService.Current.Warp.AutoRecoverOnSsl = WarpAutoRecoverOnSsl;
        _settingsService.Current.Warp.ConfirmDisconnectDuringAutoTrack = WarpConfirmDisconnectDuringAutoTrack;
        _settingsService.Current.Warp.ExecutablePath = string.IsNullOrWhiteSpace(WarpExecutablePath)
            ? null
            : WarpExecutablePath.Trim();
        _settingsService.Current.Warp.ConnectTimeoutSeconds = Math.Clamp(WarpConnectTimeoutSeconds, 5, 120);
        WarpConnectTimeoutSeconds = _settingsService.Current.Warp.ConnectTimeoutSeconds;
    }

    private void RefreshWarpCliStatus()
    {
        WarpCliResolvedPath = _warpCliService.ResolvedExecutablePath;
        WarpCliAvailable = _warpCliService.IsAvailable;
        WarpCliAvailabilityLabel = WarpCliAvailable ? "Found" : "Not found";
    }

    private void ApplyBackupSettings()
    {
        var backup = _settingsService.Current.Backup ??= new BackupSettings();
        backup.Enabled = BackupEnabled;
        var defaultCredentialsPath = Path.Combine(
            _settingsService.Current.StateFolder,
            AppConstants.BackupGoogleDriveFolderName,
            AppConstants.BackupCredentialsFileName);
        backup.CredentialsFilePath = string.IsNullOrWhiteSpace(BackupCredentialsFilePath) ||
            string.Equals(BackupCredentialsFilePath.Trim(), defaultCredentialsPath, StringComparison.OrdinalIgnoreCase)
            ? null
            : BackupCredentialsFilePath.Trim();
        BackupCredentialsFilePath = _googleDriveClient.CredentialsFilePath;
        backup.DailyBackupHour = Math.Clamp(
            BackupDailyBackupHour,
            AppConstants.MinDailyBackupHour,
            AppConstants.MaxDailyBackupHour);
        backup.EventDebounceMinutes = Math.Clamp(
            BackupEventDebounceMinutes,
            AppConstants.MinEventDebounceMinutes,
            AppConstants.MaxEventDebounceMinutes);
        backup.DbThrottleHours = Math.Clamp(
            BackupDbThrottleHours,
            AppConstants.MinDbThrottleHours,
            AppConstants.MaxDbThrottleHours);
        backup.HistoryRetentionCount = Math.Clamp(
            BackupHistoryRetentionCount,
            AppConstants.MinHistoryRetentionCount,
            AppConstants.MaxHistoryRetentionCount);

        BackupDailyBackupHour = backup.DailyBackupHour;
        BackupEventDebounceMinutes = backup.EventDebounceMinutes;
        BackupDbThrottleHours = backup.DbThrottleHours;
        BackupHistoryRetentionCount = backup.HistoryRetentionCount;
    }

    private void RefreshBackupConnectionStatus()
    {
        BackupIsConnected = _googleDriveClient.HasStoredCredential;
        BackupConnectionStatusLabel = BackupIsConnected ? "Connected" : "Not connected";
    }

    private void RefreshBackupLastRunLabel()
    {
        var backup = _settingsService.Current.Backup;
        if (backup.LastBackupUtc is null)
        {
            BackupLastRunLabel = "Never backed up.";
            return;
        }

        var when = backup.LastBackupUtc.Value.ToLocalTime().ToString("g");
        BackupLastRunLabel = backup.LastBackupSucceeded == false
            ? $"Last attempt {when} failed: {backup.LastBackupError}"
            : $"Last backup succeeded {when}.";
    }

    [RelayCommand]
    private void BrowseBackupCredentialsFile()
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = "Select Google OAuth client JSON",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (!string.IsNullOrWhiteSpace(BackupCredentialsFilePath) && File.Exists(BackupCredentialsFilePath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(BackupCredentialsFilePath);
            dialog.FileName = Path.GetFileName(BackupCredentialsFilePath);
        }

        if (dialog.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        BackupCredentialsFilePath = dialog.FileName;
    }

    [RelayCommand]
    private async Task ConnectGoogleDrive()
    {
        ApplyBackupSettings();
        _settingsService.Save();

        if (!File.Exists(BackupCredentialsFilePath))
        {
            StatusMessage = $"Google OAuth client JSON not found at {BackupCredentialsFilePath}. Browse to select the file first.";
            return;
        }

        try
        {
            StatusMessage = "Opening browser to connect Google Drive...";
            var result = await _googleDriveClient.ConnectAsync();
            RefreshBackupConnectionStatus();
            StatusMessage = result.Message;
            if (!result.Succeeded)
            {
                _logger.Warning(result.Message, LogTarget.All);
            }
        }
        catch (Exception ex)
        {
            RefreshBackupConnectionStatus();
            StatusMessage = $"Google Drive connection failed: {ex.Message}";
            _logger.Error("Google Drive connection failed.", ex, LogTarget.All);
        }
    }

    [RelayCommand]
    private async Task BackupNow()
    {
        ApplyBackupSettings();
        _settingsService.Save();
        try
        {
            StatusMessage = "Running backup...";
            var result = await _backupService.RunBackupAsync(BackupTriggerType.Manual);
            StatusMessage = result.Summary;
            RefreshBackupLastRunLabel();
            await RefreshBackupHistory();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Backup failed: {ex.Message}";
            _logger.Error("Manual backup failed.", ex, LogTarget.All);
        }
    }

    [RelayCommand]
    private async Task RefreshBackupHistory()
    {
        try
        {
            var history = await _backupService.ListHistoryAsync();
            BackupHistory.Clear();
            foreach (var file in history)
            {
                BackupHistory.Add(new BackupHistoryItemViewModel
                {
                    Id = file.Id,
                    Name = file.Name,
                    CreatedTimeUtc = file.CreatedTimeUtc
                });
            }

            SelectedBackupHistoryItem = BackupHistory.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load backup history: {ex.Message}";
            _logger.Error("Failed to load backup history.", ex, LogTarget.All);
        }
    }

    [RelayCommand]
    private async Task RestoreSelectedBackup()
    {
        if (SelectedBackupHistoryItem is null)
        {
            StatusMessage = "Select a backup to restore first.";
            return;
        }

        var confirmed = System.Windows.MessageBox.Show(
            $"Restore database and recipes from '{SelectedBackupHistoryItem.DisplayLabel}'? This overwrites the current database and recipes on this machine. Settings from the backup will be saved separately for review, not applied automatically.",
            "Confirm restore",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirmed != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            StatusMessage = "Restoring backup...";
            var reviewSettingsPath = await _backupService.RestoreAsync(SelectedBackupHistoryItem.Id);
            StatusMessage = $"Restore complete. Backed-up settings saved to {reviewSettingsPath} for manual review.";
            _databaseService.Initialize(_settingsService.Current.StateFolder);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Restore failed: {ex.Message}";
            _logger.Error("Restore failed.", ex, LogTarget.All);
        }
    }

    private void ApplyGeminiSettings()
    {
        _settingsService.Current.Gemini ??= new GeminiSettings();
        _settingsService.Current.Gemini.Enabled = GeminiEnabled;
        _settingsService.Current.Gemini.ApiKey = string.IsNullOrWhiteSpace(GeminiApiKey) ? null : GeminiApiKey.Trim();
        _settingsService.Current.Gemini.Model = NormalizeGeminiModel(GeminiModel);
        var primaryModel = _settingsService.Current.Gemini.Model;
        _settingsService.Current.Gemini.FallbackModels = GeminiFallbackModels
            .Select(option => option.Id)
            .Where(model => !string.Equals(model, primaryModel, StringComparison.OrdinalIgnoreCase))
            .Take(AppConstants.GeminiMaxFallbackModels)
            .ToArray();
    }

    private void ReloadGeminiFallbackModels()
    {
        var primary = NormalizeGeminiModel(GeminiModel);
        var fallbacks = _settingsService.Current.Gemini?.FallbackModels ?? [];
        if (fallbacks.Length == 0)
        {
            fallbacks = _geminiModelCatalog.FallbackModels.ToArray();
        }

        GeminiFallbackModels.Clear();
        foreach (var fallbackId in fallbacks.Take(AppConstants.GeminiMaxFallbackModels))
        {
            var normalized = NormalizeGeminiModel(fallbackId);
            if (string.Equals(normalized, primary, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entry = _geminiModelCatalog.GetModelsForUi()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, normalized, StringComparison.OrdinalIgnoreCase));
            GeminiFallbackModels.Add(new GeminiModelOptionViewModel
            {
                Id = normalized,
                DisplayLabel = entry?.GetDisplayLabel() ?? normalized
            });
        }

        ReloadGeminiFallbackPickerOptions();
        UpdateGeminiModelChainSummary();
        AddGeminiFallbackCommand.NotifyCanExecuteChanged();
    }

    private void ReloadGeminiFallbackPickerOptions()
    {
        var primary = NormalizeGeminiModel(GeminiModel);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primary };
        foreach (var fallback in GeminiFallbackModels)
        {
            usedIds.Add(fallback.Id);
        }

        GeminiFallbackPickerOptions.Clear();
        foreach (var entry in _geminiModelCatalog.GetTextMappingModelsForUi())
        {
            if (usedIds.Contains(entry.Id))
            {
                continue;
            }

            GeminiFallbackPickerOptions.Add(new GeminiModelOptionViewModel
            {
                Id = entry.Id,
                DisplayLabel = entry.GetDisplayLabel()
            });
        }
    }

    private void UpdateGeminiModelChainSummary()
    {
        var parts = new List<string> { NormalizeGeminiModel(GeminiModel) };
        parts.AddRange(GeminiFallbackModels.Select(option => option.Id));
        GeminiModelChainSummary = string.Join(" → ", parts);
    }

    private void ReloadGeminiModelOptions()
    {
        var selectedId = NormalizeGeminiModel(GeminiModel);
        GeminiModelOptions.Clear();
        foreach (var entry in _geminiModelCatalog.GetModelsForUi())
        {
            GeminiModelOptions.Add(new GeminiModelOptionViewModel
            {
                Id = entry.Id,
                DisplayLabel = entry.GetDisplayLabel()
            });
        }

        SelectedGeminiModel = GeminiModelOptions.FirstOrDefault(option =>
            string.Equals(option.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? GeminiModelOptions.FirstOrDefault(option =>
                !option.DisplayLabel.Contains("(deprecated)", StringComparison.OrdinalIgnoreCase))
            ?? GeminiModelOptions.FirstOrDefault();

        if (SelectedGeminiModel is not null &&
            !string.Equals(GeminiModel, SelectedGeminiModel.Id, StringComparison.OrdinalIgnoreCase))
        {
            GeminiModel = SelectedGeminiModel.Id;
        }
    }

    private void RefreshGeminiUsageLabel()
    {
        GeminiDailyUsageLabel = $"{_geminiQuotaTracker.RequestsToday}/{_geminiQuotaTracker.DailyLimit} today";
    }

    private void ApplyAutoTorrentSettings()
    {
        _settingsService.Current.AutoTorrent.QbittorrentWebUiUrl = string.IsNullOrWhiteSpace(QbittorrentWebUiUrl)
            ? "http://localhost:8080"
            : QbittorrentWebUiUrl.Trim();
        _settingsService.Current.AutoTorrent.Username = string.IsNullOrWhiteSpace(QbittorrentUsername) ? null : QbittorrentUsername.Trim();
        _settingsService.Current.AutoTorrent.Password = string.IsNullOrWhiteSpace(QbittorrentPassword) ? null : QbittorrentPassword;
        _settingsService.Current.AutoTorrent.ConfirmCloseViewer = QbittorrentConfirmCloseViewer;
        _settingsService.Current.AutoTorrent.AutoCloseViewerOnBackground = QbittorrentAutoCloseViewerOnBackground;
        _settingsService.Current.AutoTorrent.DownloadFolder = string.IsNullOrWhiteSpace(AutoTorrentDownloadFolder)
            ? null
            : AutoTorrentDownloadFolder.Trim();
        _settingsService.Current.AutoTorrent.DownloadFolders = AutoTorrentDownloadFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(_settingsService.Current.AutoTorrent.DownloadFolder) &&
            !_settingsService.Current.AutoTorrent.DownloadFolders.Contains(_settingsService.Current.AutoTorrent.DownloadFolder, StringComparer.OrdinalIgnoreCase))
        {
            _settingsService.Current.AutoTorrent.DownloadFolders.Insert(0, _settingsService.Current.AutoTorrent.DownloadFolder);
        }
        _settingsService.Current.AutoTorrent.TvShowCategoryName = string.IsNullOrWhiteSpace(AutoTorrentTvShowCategoryName)
            ? AppConstants.QbittorrentTvShowCategory
            : AutoTorrentTvShowCategoryName.Trim();
        _settingsService.Current.AutoTorrent.MovieCategoryName = string.IsNullOrWhiteSpace(AutoTorrentMovieCategoryName)
            ? AppConstants.QbittorrentMovieCategory
            : AutoTorrentMovieCategoryName.Trim();
        _settingsService.Current.AutoTorrent.AutoLinkCompletedDownloads = AutoLinkCompletedDownloads;

        var processRestart = _settingsService.Current.AutoTorrent.ProcessRestart ??= new QbittorrentProcessRestartSettings();
        processRestart.Enabled = QbittorrentProcessRestartEnabled;
        processRestart.ExecutablePath = string.IsNullOrWhiteSpace(QbittorrentProcessRestartExecutablePath)
            ? QbittorrentProcessRestartSettings.DefaultExecutablePath
            : QbittorrentProcessRestartExecutablePath.Trim();
        processRestart.GracefulShutdownSeconds = Math.Clamp(
            QbittorrentProcessRestartGracefulShutdownSeconds,
            QbittorrentProcessRestartSettings.MinGracefulShutdownSeconds,
            QbittorrentProcessRestartSettings.MaxGracefulShutdownSeconds);
        processRestart.CooldownMinutes = Math.Clamp(
            QbittorrentProcessRestartCooldownMinutes,
            QbittorrentProcessRestartSettings.MinCooldownMinutes,
            QbittorrentProcessRestartSettings.MaxCooldownMinutes);
        processRestart.MaxRestartsPerHour = Math.Clamp(
            QbittorrentProcessRestartMaxRestartsPerHour,
            QbittorrentProcessRestartSettings.MinMaxRestartsPerHour,
            QbittorrentProcessRestartSettings.MaxMaxRestartsPerHour);
        processRestart.WebUiReadyTimeoutSeconds = Math.Clamp(
            QbittorrentProcessRestartWebUiReadyTimeoutSeconds,
            QbittorrentProcessRestartSettings.MinWebUiReadyTimeoutSeconds,
            QbittorrentProcessRestartSettings.MaxWebUiReadyTimeoutSeconds);

        QbittorrentProcessRestartGracefulShutdownSeconds = processRestart.GracefulShutdownSeconds;
        QbittorrentProcessRestartCooldownMinutes = processRestart.CooldownMinutes;
        QbittorrentProcessRestartMaxRestartsPerHour = processRestart.MaxRestartsPerHour;
        QbittorrentProcessRestartWebUiReadyTimeoutSeconds = processRestart.WebUiReadyTimeoutSeconds;
        QbittorrentProcessRestartExecutablePath = processRestart.ExecutablePath;
    }

    private void ApplyLogSettings()
    {
        _settingsService.Current.Logs.MaxLinesPerFile = Math.Clamp(
            LogMaxLinesPerFile,
            AppConstants.MinLogLinesPerFile,
            AppConstants.MaxConfigurableLogLinesPerFile);
        _settingsService.Current.Logs.CleanupRetentionDays = Math.Clamp(
            LogCleanupRetentionDays,
            AppConstants.MinLogCleanupRetentionDays,
            AppConstants.MaxLogCleanupRetentionDays);

        LogMaxLinesPerFile = _settingsService.Current.Logs.MaxLinesPerFile;
        LogCleanupRetentionDays = _settingsService.Current.Logs.CleanupRetentionDays;
        _settingsService.Current.Logs.AutoCloseConsoleOnBackground = LogAutoCloseConsoleOnBackground;
    }

    private void ApplyStartupSettings()
    {
        _settingsService.Current.Startup.RunAtStartup = RunAtStartup;
        _settingsService.Current.Startup.StartMinimized = StartMinimized;
        _settingsService.Current.Startup.CloseToTray = CloseToTray;
    }

    private void LoadNotificationPreferences()
    {
        var settings = _settingsService.Current.Notifications ??= new NotificationSettings();
        NotificationPreferences.Clear();
        foreach (var definition in NotificationCatalog.Preferences)
        {
            NotificationPreferences.Add(new NotificationPreferenceItemViewModel(
                definition.Kind,
                definition.Title,
                definition.Description,
                NotificationCatalog.IsEnabled(settings, definition.Kind)));
        }
    }

    private void ApplyNotificationSettings()
    {
        _settingsService.Current.Notifications ??= new NotificationSettings();
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in NotificationPreferences)
        {
            map[item.Kind.ToString()] = item.IsEnabled;
        }

        _settingsService.Current.Notifications.EnabledByKind = map;
    }

    private void ApplyUiSettings()
    {
        _settingsService.Current.Ui ??= new UiSettings();
        _settingsService.Current.Ui.Theme = SelectedTheme;
    }

    private void ApplySymlinkSettings()
    {
        _settingsService.Current.Symlink ??= new SymlinkSettings();
        _settingsService.Current.Symlink.Enabled = SymlinkEnabled;
        _settingsService.Current.Symlink.UnifiedRoot = string.IsNullOrWhiteSpace(SymlinkUnifiedRoot)
            ? AppConstants.DefaultSymlinkUnifiedRoot
            : SymlinkUnifiedRoot.Trim();
        _settingsService.Current.Symlink.SyncOnStartup = SymlinkSyncOnStartup;
        SymlinkUnifiedRoot = _settingsService.Current.Symlink.UnifiedRoot;
    }

    private void ApplyAutoTrackSettings()
    {
        _settingsService.Current.AutoTrack ??= new AutoTrackSettings();
        var autoTrack = _settingsService.Current.AutoTrack;
        autoTrack.Enabled = AutoTrackEnabled;
        autoTrack.EnforceGlobalWeeklySchedule = AutoTrackEnforceGlobalWeeklySchedule;
        autoTrack.AnchorDayOfWeek = AutoTrackAnchorDay;
        autoTrack.AnchorTimeLocal = string.IsNullOrWhiteSpace(AutoTrackAnchorTimeLocal) ? "21:00" : AutoTrackAnchorTimeLocal.Trim();
        autoTrack.TmdbCheckIntervalMinutes = Math.Clamp(AutoTrackTmdbCheckIntervalMinutes, 5, 1440);
        autoTrack.TorrentHuntIntervalMinutes = Math.Clamp(AutoTrackTorrentHuntIntervalMinutes, 15, 1440);
        autoTrack.HuntMinHoursAfterAirDate = Math.Clamp(AutoTrackHuntMinHoursAfterAirDate, 0, 48);
        autoTrack.ReconcileIntervalMinutes = Math.Clamp(AutoTrackReconcileIntervalMinutes, 5, 1440);
        autoTrack.MaxTmdbRefreshesPerDay = Math.Clamp(AutoTrackMaxTmdbRefreshesPerDay, 1, 500);
        autoTrack.Quality ??= new AutoTrackQualityPolicy();
        autoTrack.Quality.MinQuality = string.IsNullOrWhiteSpace(AutoTrackMinQuality) ? null : AutoTrackMinQuality.Trim();
        autoTrack.Quality.MinSeeders = Math.Max(0, AutoTrackMinSeeders);
        autoTrack.Quality.MinFileSizeMb = AutoTrackMinFileSizeMb > 0 ? AutoTrackMinFileSizeMb : null;
        autoTrack.Quality.MaxFileSizeMb = AutoTrackMaxFileSizeMb > 0 ? AutoTrackMaxFileSizeMb : null;
        autoTrack.Quality.AllowedQualities = string.IsNullOrWhiteSpace(AutoTrackAllowedQualities)
            ? null
            : AutoTrackAllowedQualities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        autoTrack.Search ??= new AutoTrackSearchSettings();
        autoTrack.Search.MaxShowsPerHuntCycle = Math.Clamp(AutoTrackMaxShowsPerHuntCycle, 1, 20);
        autoTrack.Search.MaxEpisodesPerShowPerHuntCycle = Math.Clamp(AutoTrackMaxEpisodesPerShowPerHuntCycle, 1, 50);
        autoTrack.Search.MaxParallelWorkersPerShow = Math.Clamp(AutoTrackMaxParallelWorkersPerShow, 1, 4);
        autoTrack.Search.ForceParallelEpisodeSearch = AutoTrackForceParallelEpisodeSearch;
        autoTrack.Jellyfin ??= new JellyfinRefreshSettings();
        autoTrack.Jellyfin.Enabled = AutoTrackJellyfinRefreshEnabled;
        autoTrack.Jellyfin.BaseUrl = string.IsNullOrWhiteSpace(AutoTrackJellyfinBaseUrl)
            ? "http://127.0.0.1:8096"
            : AutoTrackJellyfinBaseUrl.Trim().TrimEnd('/');
        autoTrack.Jellyfin.ApiKey = string.IsNullOrWhiteSpace(AutoTrackJellyfinApiKey)
            ? null
            : AutoTrackJellyfinApiKey.Trim();
        autoTrack.Jellyfin.WarpHoldSecondsAfterNotify = Math.Clamp(
            AutoTrackJellyfinWarpHoldSeconds,
            JellyfinRefreshSettings.MinWarpHoldSecondsAfterNotify,
            JellyfinRefreshSettings.MaxWarpHoldSecondsAfterNotify);
        autoTrack.Jellyfin.EnableLogEarlyDisconnect = AutoTrackJellyfinLogEarlyDisconnectEnabled;
        autoTrack.Jellyfin.LogPath = string.IsNullOrWhiteSpace(AutoTrackJellyfinLogPath)
            ? null
            : AutoTrackJellyfinLogPath.Trim();
        autoTrack.Jellyfin.LogQuietSecondsAfterRefresh = Math.Clamp(
            AutoTrackJellyfinLogQuietSeconds,
            JellyfinRefreshSettings.MinLogQuietSecondsAfterRefresh,
            JellyfinRefreshSettings.MaxLogQuietSecondsAfterRefresh);
        autoTrack.Jellyfin.ConfirmCloseViewer = AutoTrackJellyfinConfirmCloseViewer;
        autoTrack.Jellyfin.AutoCloseViewerOnBackground = AutoTrackJellyfinAutoCloseViewerOnBackground;
        AutoTrackJellyfinBaseUrl = autoTrack.Jellyfin.BaseUrl;
        AutoTrackJellyfinWarpHoldSeconds = autoTrack.Jellyfin.WarpHoldSecondsAfterNotify;
        AutoTrackJellyfinLogPath = autoTrack.Jellyfin.LogPath;
        AutoTrackJellyfinLogQuietSeconds = autoTrack.Jellyfin.LogQuietSecondsAfterRefresh;

        AutoTrackEnforceGlobalWeeklySchedule = autoTrack.EnforceGlobalWeeklySchedule;
        AutoTrackAnchorDay = autoTrack.AnchorDayOfWeek;
        AutoTrackAnchorTimeLocal = autoTrack.AnchorTimeLocal;
        AutoTrackAnchorTime = AutoTrackWeekAnchor.ToTimePickerValue(autoTrack.AnchorTimeLocal);
        AutoTrackTmdbCheckIntervalMinutes = autoTrack.TmdbCheckIntervalMinutes;
        AutoTrackTorrentHuntIntervalMinutes = autoTrack.TorrentHuntIntervalMinutes;
        AutoTrackHuntMinHoursAfterAirDate = autoTrack.HuntMinHoursAfterAirDate;
        AutoTrackReconcileIntervalMinutes = autoTrack.ReconcileIntervalMinutes;
        AutoTrackMaxTmdbRefreshesPerDay = autoTrack.MaxTmdbRefreshesPerDay;
        AutoTrackMinSeeders = autoTrack.Quality.MinSeeders;
        AutoTrackMaxShowsPerHuntCycle = autoTrack.Search.MaxShowsPerHuntCycle;
        AutoTrackMaxEpisodesPerShowPerHuntCycle = autoTrack.Search.MaxEpisodesPerShowPerHuntCycle;
        AutoTrackMaxParallelWorkersPerShow = autoTrack.Search.MaxParallelWorkersPerShow;
    }

    private void EnsureTrayInitialized()
    {
        if (!StartMinimized && !CloseToTray)
        {
            return;
        }

        if (_trayIconService.IsInitialized)
        {
            return;
        }

        if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
        {
            _trayIconService.Initialize(mainWindow);
        }
    }

    private void RefreshLibraryRootPreview()
    {
        _settingsService.Current.DefaultLibraryFolderName = string.IsNullOrWhiteSpace(DefaultLibraryFolderName)
            ? AppConstants.DefaultLibraryFolderName
            : DefaultLibraryFolderName.Trim();

        LibraryRootPreview.Clear();
        _logger.Debug($"Refreshing library root preview for {SourceFolders.Count} source folder(s)", LogTarget.File | LogTarget.Console);
        foreach (var root in _libraryPathResolver.GetPreviewRoots(SourceFolders))
        {
            LibraryRootPreview.Add($"{Path.GetPathRoot(root)} -> {root}");
            LibraryRootPreview.Add($"    Shows: {Path.Combine(root, AppConstants.ShowsFolderName)}");
            LibraryRootPreview.Add($"    Movies: {Path.Combine(root, AppConstants.MoviesFolderName)}");
            _logger.Debug($"Library root preview generated: {root}", LogTarget.File | LogTarget.Console);
        }
    }

    private void RefreshSymlinkPreview()
    {
        var root = string.IsNullOrWhiteSpace(SymlinkUnifiedRoot)
            ? AppConstants.DefaultSymlinkUnifiedRoot
            : SymlinkUnifiedRoot.Trim();
        SymlinkPreviewShowsPath = Path.Combine(root, AppConstants.ShowsFolderName);
        SymlinkPreviewMoviesPath = Path.Combine(root, AppConstants.MoviesFolderName);
    }

    private void RefreshAdministratorStatus()
    {
        IsRunningAsAdministrator = _symlinkService.IsRunningAsAdministrator();
        AdministratorStatusLabel = IsRunningAsAdministrator ? "Running as Administrator" : "Not elevated";
    }

    private string NormalizeGeminiModel(string? model) => _geminiModelCatalog.Normalize(model);

    private static string FormatGeminiTestError(Exception ex)
    {
        if (ex.Message.Contains("404", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "Selected Gemini model is not available. Save settings after choosing a supported model.";
        }

        if (ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase))
        {
            return "Gemini rate limited (HTTP 429). Wait a minute, then retry or pick another model.";
        }

        return $"Gemini connection failed: {ex.Message}";
    }

    private static string? BrowseFolder(string? initialFolder, string description)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(initialFolder) ? initialFolder : string.Empty
        };

        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    [RelayCommand]
    private void BrowseQbittorrentExecutablePath()
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = "Select qbittorrent.exe",
            Filter = "qBittorrent (qbittorrent.exe)|qbittorrent.exe|Executable (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(QbittorrentProcessRestartExecutablePath))
        {
            try
            {
                if (File.Exists(QbittorrentProcessRestartExecutablePath))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(QbittorrentProcessRestartExecutablePath);
                    dialog.FileName = Path.GetFileName(QbittorrentProcessRestartExecutablePath);
                }
                else
                {
                    var directory = Path.GetDirectoryName(QbittorrentProcessRestartExecutablePath);
                    if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    {
                        dialog.InitialDirectory = directory;
                    }
                }
            }
            catch
            {
                // Ignore invalid initial path.
            }
        }

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
        {
            return;
        }

        QbittorrentProcessRestartExecutablePath = dialog.FileName;
    }

    [RelayCommand]
    private void BrowseWarpExecutablePath()
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = "Select warp-cli.exe",
            Filter = "warp-cli (warp-cli.exe)|warp-cli.exe|Executable (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(WarpExecutablePath))
        {
            try
            {
                if (File.Exists(WarpExecutablePath))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(WarpExecutablePath);
                    dialog.FileName = Path.GetFileName(WarpExecutablePath);
                }
                else
                {
                    var directory = Path.GetDirectoryName(WarpExecutablePath);
                    if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    {
                        dialog.InitialDirectory = directory;
                    }
                }
            }
            catch
            {
                // Ignore invalid initial path.
            }
        }
        else if (!string.IsNullOrWhiteSpace(WarpCliResolvedPath))
        {
            try
            {
                var directory = Path.GetDirectoryName(WarpCliResolvedPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }
            }
            catch
            {
                // Ignore invalid resolved path.
            }
        }

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
        {
            return;
        }

        WarpExecutablePath = dialog.FileName;
    }

    [RelayCommand]
    private void ToggleTmdbTokenVisibility()
    {
        IsTmdbTokenVisible = !IsTmdbTokenVisible;
        RefreshTokenMasks();
    }

    [RelayCommand]
    private void ToggleGeminiApiKeyVisibility()
    {
        IsGeminiApiKeyVisible = !IsGeminiApiKeyVisible;
        RefreshTokenMasks();
    }

    [RelayCommand]
    private void ToggleJellyfinApiKeyVisibility()
    {
        IsJellyfinApiKeyVisible = !IsJellyfinApiKeyVisible;
        RefreshTokenMasks();
    }

    partial void OnTmdbReadAccessTokenChanged(string? value)
    {
        RefreshTokenMasks();
    }

    partial void OnGeminiApiKeyChanged(string? value)
    {
        RefreshTokenMasks();
    }

    partial void OnAutoTrackJellyfinApiKeyChanged(string? value)
    {
        RefreshTokenMasks();
    }

    private void RefreshTokenMasks()
    {
        TmdbReadAccessTokenMasked = MaskSecret(TmdbReadAccessToken);
        GeminiApiKeyMasked = MaskSecret(GeminiApiKey);
        AutoTrackJellyfinApiKeyMasked = MaskSecret(AutoTrackJellyfinApiKey);
    }

    private static string MaskSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.Length <= 8)
        {
            return new string('•', value.Length);
        }

        return value[..4] + new string('•', value.Length - 8) + value[^4..];
    }
}
