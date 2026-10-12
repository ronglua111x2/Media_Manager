namespace media_management_app.Models;

public sealed class AppStartupSettings
{
    public bool RunAtStartup { get; set; }

    public bool StartMinimized { get; set; }

    public bool CloseToTray { get; set; }

    /// <summary>
    /// Null means the property was missing from settings.json (legacy install).
    /// Load treats null as already set up so the live library is not gated.
    /// New settings files write false until Host setup Finish.
    /// </summary>
    public bool? SetupCompleted { get; set; }

    /// <summary>
    /// Null means legacy (do not show the News optional-setup reminder).
    /// False shows the reminder after first-run skipped optional steps.
    /// </summary>
    public bool? SetupReminderDismissed { get; set; }
}
