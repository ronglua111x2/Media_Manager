using System.Net.Http;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed class SystemSettingsViewModel : SettingsViewModel
{
    public SystemSettingsViewModel(
        ISettingsService settingsService,
        IDatabaseService databaseService,
        ILibraryPathResolver libraryPathResolver,
        IQbittorrentClient qbittorrentClient,
        IWindowsStartupService windowsStartupService,
        ITrayIconService trayIconService,
        IWindowsNotificationService windowsNotificationService,
        HttpClient httpClient,
        IAppLogger logger)
        : base(settingsService, databaseService, libraryPathResolver, qbittorrentClient, windowsStartupService, trayIconService, windowsNotificationService, httpClient, logger)
    {
    }
}
