using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IAppLogger _logger;

    public MainViewModel(SettingsViewModel settingsViewModel, InboxViewModel inboxViewModel, ReviewViewModel reviewViewModel, IAppLogger logger)
    {
        _logger = logger;
        SettingsViewModel = settingsViewModel;
        InboxViewModel = inboxViewModel;
        ReviewViewModel = reviewViewModel;
        UiLogs = _logger.UiLogs;
        CurrentView = InboxViewModel;
    }

    public SettingsViewModel SettingsViewModel { get; }

    public InboxViewModel InboxViewModel { get; }

    public ReviewViewModel ReviewViewModel { get; }

    public ObservableCollection<string> UiLogs { get; }

    [ObservableProperty]
    private ViewModelBase currentView;

    [RelayCommand]
    private void ShowSettings() => CurrentView = SettingsViewModel;

    [RelayCommand]
    private void ShowInbox() => CurrentView = InboxViewModel;

    [RelayCommand]
    private void ShowReview() => CurrentView = ReviewViewModel;
}
