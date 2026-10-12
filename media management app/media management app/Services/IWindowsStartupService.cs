namespace media_management_app.Services;

public interface IWindowsStartupService
{
    bool IsRegistered();

    void SetEnabled(bool enabled);
}
