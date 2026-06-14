namespace media_management_app.Common;

public static class AppConstants
{
    #region Paths

    public const string DefaultStateFolder = @"D:\MediaManagerState";
    public const string DefaultLibraryFolderName = "MediaManagerLibrary";
    public const string ShowsFolderName = "Shows";
    public const string MoviesFolderName = "Movies";
    public const string LogFolderName = "logs";

    public const string WindowsStartupRegistryValueName = "MediaManager";

    public const string WindowsNotificationAppUserModelId = "MediaManager.Desktop";

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
