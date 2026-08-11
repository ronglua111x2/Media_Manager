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
    public const string PosterStillsFolderName = "stills";

    public const string DefaultWarpCliPath =
        @"C:\Program Files\Cloudflare\Cloudflare WARP\warp-cli.exe";

    public const string WindowsStartupRegistryValueName = "MediaManager";

    public const string StartupTaskName = @"MediaManager\AutoStart";

    public const string WindowsNotificationAppUserModelId = "MediaManager.Desktop";

    /// <summary>TMDB/Jellyfin specials season (Season 00 / S00Exx).</summary>
    public const int SpecialsSeasonNumber = 0;

    /// <summary>Jellyfin extras folder for unmatched pack orphans.</summary>
    public const string OrphanExtrasFolderName = "Season Unknown";

    #endregion

    #region Backup

    public const string BackupGoogleDriveFolderName = "GoogleDrive";
    public const string BackupCredentialsFileName = "credentials.json";
    public const string BackupTokenFolderName = "token";
    public const string BackupDriveRootFolderName = "MediaManagerBackups";
    public const string BackupDriveHistoryFolderName = "history";
    public const string BackupLatestFileName = "latest.zip";
    public const string BackupManifestFileName = "manifest.json";
    public const string BackupDatabaseEntryName = "media-manager.db";
    public const string BackupSettingsEntryName = "settings.json";
    public const string BackupRecipesEntryFolderName = "Recipes";
    public const int MinDailyBackupHour = 0;
    public const int MaxDailyBackupHour = 23;
    public const int MinEventDebounceMinutes = 5;
    public const int MaxEventDebounceMinutes = 240;
    public const int MinDbThrottleHours = 1;
    public const int MaxDbThrottleHours = 24;
    public const int MinHistoryRetentionCount = 1;
    public const int MaxHistoryRetentionCount = 200;
    public static readonly TimeSpan BackupPollInterval = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan GoogleDriveConnectTimeout = TimeSpan.FromMinutes(2);

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
