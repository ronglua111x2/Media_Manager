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
        InboxViewModel inboxViewModel,
        AutoTorrentViewModel autoTorrentViewModel,
        ReviewViewModel reviewViewModel,
        IAppLogger logger,
        IOperationProgressService progressService)
    {
        _logger = logger;
        _progressService = progressService;
        SettingsViewModel = settingsViewModel;
        InboxViewModel = inboxViewModel;
        AutoTorrentViewModel = autoTorrentViewModel;
        ReviewViewModel = reviewViewModel;
        UiLogs = _logger.UiLogs;
        _progressService.ProgressChanged += OnProgressChanged;
        SyncProgress();
        CurrentView = InboxViewModel;
    }

    public SettingsViewModel SettingsViewModel { get; }

    public InboxViewModel InboxViewModel { get; }

    public AutoTorrentViewModel AutoTorrentViewModel { get; }

    public ReviewViewModel ReviewViewModel { get; }

    public ObservableCollection<string> UiLogs { get; }

    [ObservableProperty]
    private ViewModelBase currentView;

    [ObservableProperty]
    private double operationProgressPercent;

    [ObservableProperty]
    private string operationProgressMessage = "Idle";

    [RelayCommand]
    private void ShowSettings() => CurrentView = SettingsViewModel;

    [RelayCommand]
    private void ShowInbox() => CurrentView = InboxViewModel;

    [RelayCommand]
    private void ShowAutoTorrent() => CurrentView = AutoTorrentViewModel;

    [RelayCommand]
    private void ShowReview() => CurrentView = ReviewViewModel;

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
