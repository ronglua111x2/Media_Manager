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
        IWarpCliService warpCliService,
        IWindowsStartupService windowsStartupService,
        ITrayIconService trayIconService,
        IWindowsNotificationService windowsNotificationService,
        HttpClient httpClient,
        IAppLogger logger)
        : base(settingsService, databaseService, libraryPathResolver, qbittorrentClient, warpCliService, windowsStartupService, trayIconService, windowsNotificationService, httpClient, logger)
    {
    }
}
