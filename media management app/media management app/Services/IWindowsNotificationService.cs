using media_management_app.Models;

namespace media_management_app.Services;

public interface IWindowsNotificationService
{
    void Initialize();

    bool TryShow(WindowsNotificationRequest request);

    bool TryShow(string title, string message, string? tag = null);
}
