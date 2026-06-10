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
    private readonly DispatcherTimer _statusTimer;
    private readonly Dictionary<AppWorkspaceKind, ViewModelBase> _workspaceMap;

    public MainViewModel(
        FindAddViewModel findAddViewModel,
        LibraryViewModel libraryViewModel,
        TorrentWorkspaceViewModel torrentWorkspaceViewModel,
        QbittorrentWorkspaceViewModel qbittorrentWorkspaceViewModel,
        RecipeWorkspaceViewModel recipeWorkspaceViewModel,
        SystemSettingsViewModel systemSettingsViewModel,
        IDeviceStatusService deviceStatusService,
        IConsoleWindowService consoleWindowService)
    {
        _deviceStatusService = deviceStatusService;
        _consoleWindowService = consoleWindowService;
        _workspaceMap = new Dictionary<AppWorkspaceKind, ViewModelBase>
        {
            [AppWorkspaceKind.FindAdd] = findAddViewModel,
            [AppWorkspaceKind.Library] = libraryViewModel,
            [AppWorkspaceKind.Torrent] = torrentWorkspaceViewModel,
            [AppWorkspaceKind.Qbittorrent] = qbittorrentWorkspaceViewModel,
            [AppWorkspaceKind.Recipe] = recipeWorkspaceViewModel,
            [AppWorkspaceKind.SystemSettings] = systemSettingsViewModel
        };

        NavigationItems =
        [
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
                Kind = AppWorkspaceKind.Qbittorrent,
                Label = "qBittorrent",
                Description = "Embedded Web UI",
                IconKind = "Globe"
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
        NavigateTo(AppWorkspaceKind.FindAdd);

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _statusTimer.Start();
        _ = RefreshStatusAsync();
    }

    public ObservableCollection<ShellNavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private ViewModelBase currentView = null!;

    [ObservableProperty]
    private AppWorkspaceKind selectedWorkspace;

    [ObservableProperty]
    private string selectedWorkspaceLabel = "Find/Add";

    [ObservableProperty]
    private string storageStatus = "Storage: checking...";

    [ObservableProperty]
    private string qbittorrentStatus = "qBittorrent: checking...";

    [ObservableProperty]
    private string jobStatus = "Jobs: idle";

    [ObservableProperty]
    private bool hasLowSpace;

    [ObservableProperty]
    private bool isQbittorrentConnected;

    [ObservableProperty]
    private bool isSidebarCollapsed = true;

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
        ApplyDeviceStatus();
    }

    private void ApplyDeviceStatus()
    {
        var status = _deviceStatusService.Current;
        StorageStatus = status.StorageSummary;
        QbittorrentStatus = status.QbittorrentStatus;
        JobStatus = status.JobStatus;
        HasLowSpace = status.HasLowSpace;
        IsQbittorrentConnected = status.IsQbittorrentConnected;
    }
}
