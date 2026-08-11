using System.Net.Http;
using media_management_app.Services;
using media_management_app.Services.Backup;
using media_management_app.Services.Gemini;
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
        IJellyfinLibraryRefreshService jellyfinLibraryRefreshService,
        HttpClient httpClient,
        IGeminiApiClient geminiApiClient,
        IGeminiModelCatalogService geminiModelCatalog,
        GeminiQuotaTracker geminiQuotaTracker,
        IGoogleDriveClient googleDriveClient,
        IBackupService backupService,
        IAppLogger logger)
        : base(settingsService, databaseService, libraryPathResolver, qbittorrentClient, warpCliService, windowsStartupService, trayIconService, windowsNotificationService, themeService, symlinkCoordinatorService, symlinkService, jellyfinLibraryRefreshService, httpClient, geminiApiClient, geminiModelCatalog, geminiQuotaTracker, googleDriveClient, backupService, logger)
    {
    }
}
