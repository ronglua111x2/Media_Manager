using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
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
    private readonly HttpClient _httpClient;
    private readonly IAppLogger _logger;
    private bool _isLoadingSettings;

    [ObservableProperty]
    private string stateFolder = string.Empty;

    [ObservableProperty]
    private string defaultLibraryFolderName = string.Empty;

    [ObservableProperty]
    private string? tmdbReadAccessToken;

    [ObservableProperty]
    private string qbittorrentWebUiUrl = "http://localhost:8080";

    [ObservableProperty]
    private string? qbittorrentUsername;

    [ObservableProperty]
    private string? qbittorrentPassword;

    [ObservableProperty]
    private string autoTorrentDownloadFolder = string.Empty;

    [ObservableProperty]
    private string? selectedAutoTorrentDownloadFolder;

    [ObservableProperty]
    private string autoTorrentCategoryName = "AutoTorrent";

    [ObservableProperty]
    private bool autoLinkCompletedDownloads;

    [ObservableProperty]
    private int logMaxLinesPerFile = AppConstants.MaxLogLinesPerFile;

    [ObservableProperty]
    private int logCleanupRetentionDays = AppConstants.DefaultLogCleanupRetentionDays;

    [ObservableProperty]
    private bool runAtStartup;

    [ObservableProperty]
    private bool startMinimized;

    [ObservableProperty]
    private bool closeToTray;

    [ObservableProperty]
    private bool autoTrackEnabled = true;

    [ObservableProperty]
    private int autoTrackIntervalHours = 6;

    [ObservableProperty]
    private bool warpEnabled = true;

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
    private string? selectedSourceFolder;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private SettingsSection selectedSettingsSection = SettingsSection.System;

    public SettingsViewModel(
        ISettingsService settingsService,
        IDatabaseService databaseService,
        ILibraryPathResolver libraryPathResolver,
        IQbittorrentClient qbittorrentClient,
        IWarpCliService warpCliService,
        IWindowsStartupService windowsStartupService,
        ITrayIconService trayIconService,
        IWindowsNotificationService windowsNotificationService,
        HttpClient httpClient,
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
        _httpClient = httpClient;
        _logger = logger;
        SourceFolders = [];
        AutoTorrentDownloadFolders = [];
        LibraryRootPreview = [];
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
    private void Save()
    {
        _settingsService.Current.StateFolder = StateFolder;
        _settingsService.Current.SourceFolders = SourceFolders.ToList();
        _settingsService.Current.LibraryRootMode = LibraryRootMode.AutoPerDrive;
        _settingsService.Current.DefaultLibraryFolderName = string.IsNullOrWhiteSpace(DefaultLibraryFolderName)
            ? AppConstants.DefaultLibraryFolderName
            : DefaultLibraryFolderName.Trim();
        _settingsService.Current.TmdbReadAccessToken = string.IsNullOrWhiteSpace(TmdbReadAccessToken) ? null : TmdbReadAccessToken;
        ApplyAutoTorrentSettings();
        ApplyWarpSettings();
        ApplyLogSettings();
        ApplyStartupSettings();
        ApplyAutoTrackSettings();
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
            Tag = null,
            HeroImagePathOrUrl = string.IsNullOrWhiteSpace(NotificationTestImagePathOrUrl)
                ? null
                : NotificationTestImagePathOrUrl.Trim()
        });
        StatusMessage = sent
            ? "Test notification sent. Check Windows Action Center."
            : "Failed to send notification. Check Windows notification settings or Focus Assist.";
    }

    [RelayCommand]
    private void BrowseNotificationTestImage()
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = "Select cover image",
            Filter = "Image files (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (!string.IsNullOrWhiteSpace(NotificationTestImagePathOrUrl) && File.Exists(NotificationTestImagePathOrUrl))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(NotificationTestImagePathOrUrl);
            dialog.FileName = Path.GetFileName(NotificationTestImagePathOrUrl);
        }

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            NotificationTestImagePathOrUrl = dialog.FileName;
        }
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
            var connected = await _warpCliService.ConnectAsync(TimeSpan.FromSeconds(WarpConnectTimeoutSeconds));
            if (!connected)
            {
                StatusMessage = $"WARP connect test failed or timed out after {WarpConnectTimeoutSeconds}s.";
                _logger.Warning(StatusMessage, LogTarget.All);
                return;
            }

            await _warpCliService.DisconnectAsync();
            StatusMessage = "WARP connect test succeeded (disconnected after test). Save settings to persist changes.";
            _logger.Info(StatusMessage, LogTarget.All);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.Error($"WARP connection test failed: {ex.Message}", ex, LogTarget.All);
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

    partial void OnWarpExecutablePathChanged(string? value)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        ApplyWarpSettings();
        RefreshWarpCliStatus();
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
            TmdbReadAccessToken = _settingsService.Current.TmdbReadAccessToken;
            QbittorrentWebUiUrl = _settingsService.Current.AutoTorrent.QbittorrentWebUiUrl;
            QbittorrentUsername = _settingsService.Current.AutoTorrent.Username;
            QbittorrentPassword = _settingsService.Current.AutoTorrent.Password;
            AutoTorrentDownloadFolder = _settingsService.Current.AutoTorrent.DownloadFolder ?? string.Empty;
            AutoTorrentCategoryName = _settingsService.Current.AutoTorrent.CategoryName;
            AutoLinkCompletedDownloads = _settingsService.Current.AutoTorrent.AutoLinkCompletedDownloads;
            LogMaxLinesPerFile = _settingsService.Current.Logs.MaxLinesPerFile;
            LogCleanupRetentionDays = _settingsService.Current.Logs.CleanupRetentionDays;
            RunAtStartup = _settingsService.Current.Startup.RunAtStartup;
            StartMinimized = _settingsService.Current.Startup.StartMinimized;
            CloseToTray = _settingsService.Current.Startup.CloseToTray;
            AutoTrackEnabled = _settingsService.Current.AutoTrack?.Enabled ?? true;
            AutoTrackIntervalHours = _settingsService.Current.AutoTrack?.IntervalHours ?? 6;
            WarpEnabled = _settingsService.Current.Warp.Enabled;
            WarpExecutablePath = _settingsService.Current.Warp.ExecutablePath;
            WarpConnectTimeoutSeconds = _settingsService.Current.Warp.ConnectTimeoutSeconds;
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
        RefreshWarpCliStatus();
        _logger.Info($"Settings UI loaded. VisibleSourceFolders={SourceFolders.Count}, SelectedSourceFolder='{SelectedSourceFolder ?? "<none>"}'", LogTarget.All);
    }

    private void ApplyWarpSettings()
    {
        _settingsService.Current.Warp ??= new WarpSettings();
        _settingsService.Current.Warp.Enabled = WarpEnabled;
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

    private void ApplyAutoTorrentSettings()
    {
        _settingsService.Current.AutoTorrent.QbittorrentWebUiUrl = string.IsNullOrWhiteSpace(QbittorrentWebUiUrl)
            ? "http://localhost:8080"
            : QbittorrentWebUiUrl.Trim();
        _settingsService.Current.AutoTorrent.Username = string.IsNullOrWhiteSpace(QbittorrentUsername) ? null : QbittorrentUsername.Trim();
        _settingsService.Current.AutoTorrent.Password = string.IsNullOrWhiteSpace(QbittorrentPassword) ? null : QbittorrentPassword;
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
        _settingsService.Current.AutoTorrent.CategoryName = string.IsNullOrWhiteSpace(AutoTorrentCategoryName)
            ? "AutoTorrent"
            : AutoTorrentCategoryName.Trim();
        _settingsService.Current.AutoTorrent.AutoLinkCompletedDownloads = AutoLinkCompletedDownloads;
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
    }

    private void ApplyStartupSettings()
    {
        _settingsService.Current.Startup.RunAtStartup = RunAtStartup;
        _settingsService.Current.Startup.StartMinimized = StartMinimized;
        _settingsService.Current.Startup.CloseToTray = CloseToTray;
    }

    private void ApplyAutoTrackSettings()
    {
        _settingsService.Current.AutoTrack ??= new AutoTrackSettings();
        _settingsService.Current.AutoTrack.Enabled = AutoTrackEnabled;
        _settingsService.Current.AutoTrack.IntervalHours = Math.Clamp(AutoTrackIntervalHours, 1, 168);
        AutoTrackIntervalHours = _settingsService.Current.AutoTrack.IntervalHours;
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
}
