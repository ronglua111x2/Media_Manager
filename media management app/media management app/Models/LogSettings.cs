using media_management_app.Common;

namespace media_management_app.Models;

public sealed class LogSettings
{
    public int MaxLinesPerFile { get; set; } = AppConstants.MaxLogLinesPerFile;

    public int CleanupRetentionDays { get; set; } = AppConstants.DefaultLogCleanupRetentionDays;

    /// <summary>
    /// When true, the console log window is closed (not only hidden) when the app
    /// enters background mode.
    /// </summary>
    public bool AutoCloseConsoleOnBackground { get; set; } = true;

    /// <summary>
    /// Console log window view mode. Classic is the original plain-text list.
    /// </summary>
    public ConsoleViewMode ConsoleMode { get; set; } = ConsoleViewMode.Classic;

    /// <summary>
    /// Levels hidden in the console's Pretty mode (Critical follows Error).
    /// </summary>
    public List<AppLogLevel> ConsoleHiddenLevels { get; set; } = [AppLogLevel.Trace];

    public bool ConsoleWrap { get; set; }
}
