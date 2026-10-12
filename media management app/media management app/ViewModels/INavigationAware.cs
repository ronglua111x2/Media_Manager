namespace media_management_app.ViewModels;

/// <summary>
/// Workspace lifecycle hooks invoked by <see cref="MainViewModel"/> when switching tabs.
/// Singleton VMs stay alive; implement refresh/cancel policy here (Sprint 4 / E3).
/// </summary>
public interface INavigationAware
{
    void OnNavigatedTo();

    void OnNavigatedFrom();
}
