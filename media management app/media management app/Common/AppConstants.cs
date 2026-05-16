namespace media_management_app.Common;

public static class AppConstants
{
    #region Paths

    public const string DefaultStateFolder = @"D:\MediaManagerState";
    public const string DefaultLibraryFolderName = "Library";
    public const string LogFolderName = "logs";

    #endregion

    #region Logging

    public const int MaxLogLinesPerFile = 2000;
    public const int MaxUiLogLines = 500;
    public const string LogFileSuffix = "systemlog";
    public const string LogFileExtension = ".txt";
    public const string LogTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
    public const string LogFileTimestampFormat = "yyyyMMdd_HHmmss_fff";

    #endregion
}
