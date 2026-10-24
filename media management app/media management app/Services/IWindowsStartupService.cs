namespace media_management_app.Services;

public interface IWindowsStartupService
{
    bool IsRegistered();

    void SetEnabled(bool enabled);

    /// <summary>
    /// Called at app start when Run at startup is on. Re-registers the logon task when it is missing,
    /// points at another exe, or still carries the old schtasks defaults (AC-only, stop on battery, 72 h limit).
    /// A dev build under bin\ never takes over a task that points at another existing exe.
    /// </summary>
    void EnsureRegistered();
}
