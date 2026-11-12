namespace media_management_app.Common;

public static class AppConstants
{
    #region Paths

    /// <summary>
    /// Pre-bootstrap installs used this folder. Load still detects it once and writes the LocalAppData pointer.
    /// Do not use as the default for new machines.
    /// </summary>
    public const string LegacyStateFolder = @"D:\MediaManagerState";

    public const string PointerFileName = "state-path.txt";

    public const string DefaultStateFolderName = "State";

    public static string PointerDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaManager");

    public static string PointerFilePath => Path.Combine(PointerDirectory, PointerFileName);

    /// <summary>New installs: DB, logs, posters under %LocalAppData%\MediaManager\State.</summary>
    public static string DefaultStateFolder => Path.Combine(PointerDirectory, DefaultStateFolderName);

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

    public const string DefaultWarpLogDirectory =
        @"C:\ProgramData\Cloudflare\Cloudflare WARP\log";

    public const string WindowsStartupRegistryValueName = "MediaManager";

    public const string StartupTaskName = @"MediaManager\AutoStart";

    public const string WindowsNotificationAppUserModelId = "MediaManager.Desktop";

    /// <summary>TMDB/Jellyfin specials season (Season 00 / S00Exx).</summary>
    public const int SpecialsSeasonNumber = 0;

    /// <summary>
    /// Jellyfin extras folder for unmatched pack orphans (NCOP/NCED, menus, PVs, unmapped OVAs):
    /// <c>Show/Season NN/extras/</c> when the file sat in a season folder of the pack, otherwise
    /// <c>Show/extras/</c>. Jellyfin lists these under Extras instead of as episodes.
    /// </summary>
    public const string OrphanExtrasFolderName = "extras";

    /// <summary>
    /// Former orphan folder. Jellyfin read it as an unnumbered season and the files as episodes
    /// (unplayed counts, Next Up, a CRC read as an episode number). Links found there are moved.
    /// </summary>
    public const string LegacyOrphanExtrasFolderName = "Season Unknown";

    public static bool IsSpecialsSeason(int seasonNumber) =>
        seasonNumber == SpecialsSeasonNumber;

    public static string FormatSeasonShortLabel(int seasonNumber) =>
        IsSpecialsSeason(seasonNumber) ? "SP" : $"S{seasonNumber}";

    public static string FormatSeasonDisplayName(int seasonNumber) =>
        IsSpecialsSeason(seasonNumber) ? "Extras/Specials/OVAs" : $"Season {seasonNumber:00}";

    #endregion

    #region Backup

    public const string BackupGoogleDriveFolderName = "GoogleDrive";
    public const string BackupCredentialsFileName = "credentials.json";
    public const string BackupTokenFolderName = "token";
    public const string BackupDriveRootFolderName = "MediaManagerBackups";
    public const string BackupDriveHistoryFolderName = "history";
    public const string GoogleDriveFolderUrlPrefix = "https://drive.google.com/drive/folders/";
    public const string BackupLatestFileName = "latest.zip";
    public const string BackupManifestFileName = "manifest.json";
    public const string BackupDatabaseEntryName = "media-manager.db";
    public const string BackupSettingsEntryName = "settings.json";
    public const string BackupRecipesEntryFolderName = "Recipes";
    public const string BackupRestoredSettingsReviewFileName = "restored-settings.review.json";
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
    public const string DefaultGeminiModel = "gemini-3.5-flash-lite";
    public const string GeminiApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/";
    public const string GeminiRouterStateFileName = "gemini-router-state.json";
    public const string GeminiMappingCacheFileName = "gemini-mapping-cache.json";
    public const int GeminiMaxFallbackModels = 3;

    /// <summary>Most models one Gemini call may try before giving up (keeps an outage from burning quota).</summary>
    public const int GeminiMaxModelsPerCall = 3;

    /// <summary>The router waits for a model's per-minute spacing up to this long before using the next model.</summary>
    public const int GeminiMaxSpacingWaitSeconds = 12;

    public const int GeminiDefaultTimeoutSeconds = 90;
    public const int GeminiOverloadedLockSeconds = 60;
    public const int GeminiServerErrorLockSeconds = 30;

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
    public const string CrashLogFileSuffix = "crash";
    public const string LifetimeTraceFileSuffix = "lifetime";
    public const string SettingsLogFileSuffix = "settingslog";
    public const string CartDebugLogFileSuffix = "cartdebug";
    public const string CrashDumpFolderName = "CrashDumps";
    public const string SessionAliveFileName = "session.alive";
    public const string LastExitFileName = "last-exit.json";
    public static readonly TimeSpan CrashHeartbeatInterval = TimeSpan.FromSeconds(60);
    public const string LogFileExtension = ".txt";
    public const string LogTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
    public const string LogFileTimestampFormat = "yyyyMMdd_HHmmss_fff";

    #endregion
}
