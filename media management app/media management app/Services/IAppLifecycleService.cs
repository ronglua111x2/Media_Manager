namespace media_management_app.Services;

public enum AppMode
{
    Foreground,
    Background
}

public interface IAppLifecycleService
{
    AppMode CurrentMode { get; }

    event EventHandler<AppMode>? AppModeChanged;

    void EnterBackgroundMode();

    void EnterForegroundMode();
}
