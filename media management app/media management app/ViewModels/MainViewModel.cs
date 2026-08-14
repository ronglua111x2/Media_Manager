using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IDeviceStatusService _deviceStatusService;
    private readonly IConsoleWindowService _consoleWindowService;
    private readonly IWarpCliService _warpCliService;
    private readonly ISettingsService _settingsService;
    private readonly ShellLaunchGuard _shellLaunchGuard;
    private readonly DispatcherTimer _statusTimer;
    private readonly Dictionary<AppWorkspaceKind, ViewModelBase> _workspaceMap;

    public MainViewModel(
        AutoTrackViewModel autoTrackViewModel,
        NewsViewModel newsViewModel,
        FindAddViewModel findAddViewModel,
        LibraryViewModel libraryViewModel,
        TorrentWorkspaceViewModel torrentWorkspaceViewModel,
        RecipeWorkspaceViewModel recipeWorkspaceViewModel,
        SystemSettingsViewModel systemSettingsViewModel,
        IDeviceStatusService deviceStatusService,
        IConsoleWindowService consoleWindowService,
        IWarpCliService warpCliService,
        ISettingsService settingsService,
        IAppLogger logger,
        IAppLifecycleService lifecycleService)
    {
        _deviceStatusService = deviceStatusService;
        _consoleWindowService = consoleWindowService;
        _warpCliService = warpCliService;
        _settingsService = settingsService;
        _shellLaunchGuard = new ShellLaunchGuard(logger);
        _workspaceMap = new Dictionary<AppWorkspaceKind, ViewModelBase>
        {
            [AppWorkspaceKind.AutoTrack] = autoTrackViewModel,
            [AppWorkspaceKind.News] = newsViewModel,
            [AppWorkspaceKind.FindAdd] = findAddViewModel,
            [AppWorkspaceKind.Library] = libraryViewModel,
            [AppWorkspaceKind.Torrent] = torrentWorkspaceViewModel,
            [AppWorkspaceKind.Recipe] = recipeWorkspaceViewModel,
            [AppWorkspaceKind.SystemSettings] = systemSettingsViewModel
        };

        NavigationItems =
        [
            new ShellNavigationItem
            {
                Kind = AppWorkspaceKind.News,
                Label = "News",
                Description = "New episodes this week",
                IconKind = "Newspaper"
            },
            new ShellNavigationItem
            {
                Kind = AppWorkspaceKind.AutoTrack,
                Label = "Auto",
                Description = "Auto-track controls",
                IconKind = "Bot"
            },
            new ShellNavigationItem
            {
                Kind = AppWorkspaceKind.FindAdd,
                Label = "Find/Add",
                Description = "Find and add media",
                IconKind = "Search"
            },
            new ShellNavigationItem
            {
                Kind = AppWorkspaceKind.Library,
                Label = "Library",
                Description = "Manage media and carts",
                IconKind = "Library"
            },
            new ShellNavigationItem
            {
                Kind = AppWorkspaceKind.Torrent,
                Label = "Torrent",
                Description = "Search and fetch torrents",
                IconKind = "Download"
            },
            new ShellNavigationItem
            {
                Kind = AppWorkspaceKind.Recipe,
                Label = "Recipe",
                Description = "Build recipe modules",
                IconKind = "ScrollText"
            },
            new ShellNavigationItem
            {
                Kind = AppWorkspaceKind.SystemSettings,
                Label = "System Settings",
                Description = "App-wide settings",
                IconKind = "Settings"
            }
        ];

        _deviceStatusService.StatusChanged += OnDeviceStatusChanged;
        ApplyDeviceStatus();
        NavigateTo(AppWorkspaceKind.News);

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _statusTimer.Start();
        _ = RefreshStatusAsync();

        lifecycleService.AppModeChanged += OnAppModeChanged;
    }

    public ObservableCollection<ShellNavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private ViewModelBase currentView = null!;

    [ObservableProperty]
    private AppWorkspaceKind selectedWorkspace;

    [ObservableProperty]
    private string selectedWorkspaceLabel = "Find/Add";

    public ObservableCollection<StorageStatusViewModel> DriveStatuses { get; } = [];

    [ObservableProperty]
    private DependencyStatusInfo qbittorrentDependency = new()
    {
        Name = "qBittorrent",
        StatusText = "checking...",
        Detail = "Checking qBittorrent status"
    };

    [ObservableProperty]
    private DependencyStatusInfo warpDependency = new()
    {
        Name = "WARP",
        StatusText = "checking...",
        Detail = "Checking WARP status"
    };

    [ObservableProperty]
    private DependencyStatusInfo jellyfinDependency = new()
    {
        Name = "Jellyfin",
        StatusText = "checking...",
        Detail = "Checking Jellyfin status"
    };

    [ObservableProperty]
    private DependencyStatusInfo googleDriveDependency = new()
    {
        Name = "Google Drive",
        StatusText = "checking...",
        Detail = "Checking Google Drive status"
    };

    [ObservableProperty]
    private bool isBackupRunning;

    [ObservableProperty]
    private string jobStatus = "Idle";

    [ObservableProperty]
    private bool isJobActive;

    [ObservableProperty]
    private bool hasLowSpace;

    [ObservableProperty]
    private bool isSidebarCollapsed = true;

    public string GoogleDriveBackupToolTip
    {
        get
        {
            if (!GoogleDriveDependency.IsConfigured)
            {
                return GoogleDriveDependency.Detail;
            }

            return string.IsNullOrWhiteSpace(GetBackupFolderId())
                ? "Backup folder not created yet (run a backup first)."
                : "Open backup folder in browser";
        }
    }

    [RelayCommand]
    private void Navigate(ShellNavigationItem? item)
    {
        if (item is null)
        {
            return;
        }

        NavigateTo(item.Kind);
    }

    [RelayCommand]
    private void OpenConsole()
    {
        _consoleWindowService.ShowConsole();
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
    }

    [RelayCommand(CanExecute = nameof(CanToggleWarp))]
    private async Task ToggleWarp()
    {
        if (!_warpCliService.IsAvailable)
        {
            return;
        }

        if (_warpCliService.LastKnownConnected || WarpDependency.IsOk)
        {
            if (ShouldConfirmWarpDisconnect() && !UserConfirmedWarpDisconnect())
            {
                return;
            }

            await _warpCliService.ForceDisconnectAsync();
        }
        else
        {
            var timeout = TimeSpan.FromSeconds(
                Math.Clamp(_settingsService.Current.Warp?.ConnectTimeoutSeconds ?? 30, 5, 120));
            await _warpCliService.AcquireAsync(WarpLeaseReason.User, timeout);
        }

        _deviceStatusService.RefreshWarpOnly();
    }

    private bool CanToggleWarp() =>
        WarpDependency.IsConfigured && _warpCliService.IsAvailable && !_warpCliService.InFlight;

    private bool ShouldConfirmWarpDisconnect() =>
        (_settingsService.Current.Warp?.ConfirmDisconnectDuringAutoTrack ?? true) &&
        _warpCliService.HasAutoTrackPipelineLease;

    private static bool UserConfirmedWarpDisconnect()
    {
        var result = System.Windows.MessageBox.Show(
            "Auto-Track turned WARP on for a job that is still running. Disconnecting may break TMDB recover, torrent hunt, or Jellyfin refresh. Disconnect anyway?",
            "WARP",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);
        return result == System.Windows.MessageBoxResult.Yes;
    }

    [RelayCommand]
    private void OpenDriveFolder(StorageStatusViewModel? status)
    {
        if (status is null)
        {
            return;
        }

        var folder = ResolveTorrentFolder(status.DriveRoot);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        _shellLaunchGuard.TryLaunch(folder, requireExistingDirectory: true);
    }

    [RelayCommand(CanExecute = nameof(CanOpenGoogleDriveBackup))]
    private void OpenGoogleDriveBackup()
    {
        var folderId = GetBackupFolderId();
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return;
        }

        _shellLaunchGuard.TryLaunch($"{AppConstants.GoogleDriveFolderUrlPrefix}{folderId}");
    }

    private bool CanOpenGoogleDriveBackup() =>
        GoogleDriveDependency.IsConfigured && !string.IsNullOrWhiteSpace(GetBackupFolderId());

    private string? GetBackupFolderId()
    {
        var backup = _settingsService.Current.Backup;
        if (backup is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(backup.DriveHistoryFolderId))
        {
            return backup.DriveHistoryFolderId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(backup.DriveMachineFolderId))
        {
            return backup.DriveMachineFolderId.Trim();
        }

        return string.IsNullOrWhiteSpace(backup.DriveRootFolderId)
            ? null
            : backup.DriveRootFolderId.Trim();
    }

    private string? ResolveTorrentFolder(string driveRoot)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            return null;
        }

        var torrent = _settingsService.Current.AutoTorrent;
        foreach (var folder in EnumerateTorrentDownloadFolders(torrent))
        {
            var root = Path.GetPathRoot(folder);
            if (string.Equals(root, driveRoot, StringComparison.OrdinalIgnoreCase))
            {
                return folder;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateTorrentDownloadFolders(AutoTorrentSettings? torrent)
    {
        if (torrent is null)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(torrent.DownloadFolder))
        {
            yield return torrent.DownloadFolder.Trim();
        }

        foreach (var folder in torrent.DownloadFolders)
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                yield return folder.Trim();
            }
        }
    }

    private void NavigateTo(AppWorkspaceKind workspace)
    {
        SelectedWorkspace = workspace;
        CurrentView = _workspaceMap[workspace];
        SelectedWorkspaceLabel = NavigationItems.First(item => item.Kind == workspace).Label;
        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.Kind == workspace;
        }
    }

    private async Task RefreshStatusAsync()
    {
        await _deviceStatusService.RefreshAsync();
    }

    private void OnDeviceStatusChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, () => OnDeviceStatusChanged(sender, e));
            return;
        }

        ApplyDeviceStatus();
    }

    private void ApplyDeviceStatus()
    {
        var status = _deviceStatusService.Current;
        DriveStatuses.Clear();
        foreach (var drive in status.DriveStatuses)
        {
            DriveStatuses.Add(new StorageStatusViewModel
            {
                DriveRoot = drive.DriveRoot,
                Folder = drive.Folder,
                OpenFolderPath = ResolveTorrentFolder(drive.DriveRoot),
                TotalBytes = drive.TotalBytes,
                FreeBytes = drive.FreeBytes
            });
        }

        QbittorrentDependency = status.Qbittorrent;
        WarpDependency = status.Warp;
        JellyfinDependency = status.Jellyfin;
        GoogleDriveDependency = status.GoogleDrive;
        IsBackupRunning = status.IsBackupRunning;
        JobStatus = status.JobStatus;
        IsJobActive = status.IsJobActive;
        HasLowSpace = status.HasLowSpace;
        ToggleWarpCommand.NotifyCanExecuteChanged();
        OpenGoogleDriveBackupCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(GoogleDriveBackupToolTip));
    }

    private void OnAppModeChanged(object? sender, AppMode mode)
    {
        if (mode == AppMode.Background)
        {
            _statusTimer.Stop();
            return;
        }

        _statusTimer.Start();
        _ = RefreshStatusAsync();
    }
}
