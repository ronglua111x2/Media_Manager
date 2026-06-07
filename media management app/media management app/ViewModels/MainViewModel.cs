using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IAppLogger _logger;
    private readonly IOperationProgressService _progressService;

    public MainViewModel(
        SettingsViewModel settingsViewModel,
        AutoTorrentViewModel autoTorrentViewModel,
        IAppLogger logger,
        IOperationProgressService progressService)
    {
        _logger = logger;
        _progressService = progressService;
        SettingsViewModel = settingsViewModel;
        AutoTorrentViewModel = autoTorrentViewModel;
        UiLogs = _logger.UiLogs;
        _progressService.ProgressChanged += OnProgressChanged;
        SyncProgress();
        CurrentView = AutoTorrentViewModel;
    }

    public SettingsViewModel SettingsViewModel { get; }

    public AutoTorrentViewModel AutoTorrentViewModel { get; }

    public ObservableCollection<string> UiLogs { get; }

    [ObservableProperty]
    private ViewModelBase currentView;

    [ObservableProperty]
    private double operationProgressPercent;

    [ObservableProperty]
    private string operationProgressMessage = "Idle";

    [ObservableProperty]
    private bool isSidebarOpen = true;

    [ObservableProperty]
    private bool isConsoleVisible = true;

    [RelayCommand]
    private void ShowSettings() => CurrentView = SettingsViewModel;

    [RelayCommand]
    private void ShowAutoTorrent() => CurrentView = AutoTorrentViewModel;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand]
    private void ToggleConsole() => IsConsoleVisible = !IsConsoleVisible;

    private void OnProgressChanged(object? sender, EventArgs e)
    {
        SyncProgress();
    }

    private void SyncProgress()
    {
        OperationProgressPercent = _progressService.Percent;
        OperationProgressMessage = _progressService.Message;
    }
}
