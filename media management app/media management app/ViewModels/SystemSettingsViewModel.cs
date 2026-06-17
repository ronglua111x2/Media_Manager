using System.Net.Http;
using media_management_app.Services;
using media_management_app.Services.Symlink;

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
        IThemeService themeService,
        ISymlinkCoordinatorService symlinkCoordinatorService,
        ISymlinkService symlinkService,
        HttpClient httpClient,
        IAppLogger logger)
        : base(settingsService, databaseService, libraryPathResolver, qbittorrentClient, warpCliService, windowsStartupService, trayIconService, windowsNotificationService, themeService, symlinkCoordinatorService, symlinkService, httpClient, logger)
    {
    }
}
