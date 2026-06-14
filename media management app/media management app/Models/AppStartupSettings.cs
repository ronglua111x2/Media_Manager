namespace media_management_app.Models;

public sealed class AppStartupSettings
{
    public bool RunAtStartup { get; set; }

    public bool StartMinimized { get; set; }

    public bool CloseToTray { get; set; }
}
