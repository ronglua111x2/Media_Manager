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
        HttpClient httpClient,
        IAppLogger logger)
        : base(settingsService, databaseService, libraryPathResolver, qbittorrentClient, httpClient, logger)
    {
    }
}
