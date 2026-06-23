namespace media_management_app.Common;

public static class AppConstants
{
    #region Paths

    public const string DefaultStateFolder = @"D:\MediaManagerState";
    public const string DefaultLibraryFolderName = "MediaManagerLibrary";
    public const string DefaultSymlinkUnifiedRoot = @"C:\JellyfinLibrary";
    public const string ShowsFolderName = "Shows";
    public const string MoviesFolderName = "Movies";
    public const string LogFolderName = "logs";
    public const string PostersFolderName = "posters";
    public const string PosterShowsFolderName = "shows";
    public const string PosterMoviesFolderName = "movies";

    public const string DefaultWarpCliPath =
        @"C:\Program Files\Cloudflare\Cloudflare WARP\warp-cli.exe";

    public const string WindowsStartupRegistryValueName = "MediaManager";

    public const string WindowsNotificationAppUserModelId = "MediaManager.Desktop";

    /// <summary>TMDB/Jellyfin specials season (Season 00 / S00Exx).</summary>
    public const int SpecialsSeasonNumber = 0;

    /// <summary>Jellyfin extras folder for unmatched pack orphans.</summary>
    public const string OrphanExtrasFolderName = "Season Unknown";

    #endregion

    #region qBittorrent

    public const string QbittorrentTvShowCategory = "TV Show";
    public const string QbittorrentMovieCategory = "Movie";

    #endregion

    #region Gemini

    public const string GeminiModelsFileName = "gemini-models.json";
    public const string DefaultGeminiModel = "gemini-2.5-flash-lite";
    public const string GeminiApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/";
    public const int GeminiDailyRequestLimit = 1500;
    public const int GeminiDailyWarningThreshold = 1200;
    public const string GeminiQuotaStateFileName = "gemini-quota.json";
    public const string GeminiMappingCacheFileName = "gemini-mapping-cache.json";
    public const int GeminiMinRequestSpacingMs = 2000;
    public const int GeminiMaxRetriesPerModel = 3;
    public const int GeminiRetryBaseDelayMs = 2000;
    public const int GeminiMaxFallbackModels = 3;

    #endregion

    #region Logging

    public const int MaxLogLinesPerFile = 2000;
    public const int MinLogLinesPerFile = 100;
    public const int MaxConfigurableLogLinesPerFile = 100000;
    public const int DefaultLogCleanupRetentionDays = 30;
    public const int MinLogCleanupRetentionDays = 1;
    public const int MaxLogCleanupRetentionDays = 3650;
    public const int MaxUiLogLines = 500;
    public const string LogFileSuffix = "systemlog";
    public const string LogFileExtension = ".txt";
    public const string LogTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
    public const string LogFileTimestampFormat = "yyyyMMdd_HHmmss_fff";

    #endregion
}
