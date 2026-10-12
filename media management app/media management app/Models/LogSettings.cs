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
}
