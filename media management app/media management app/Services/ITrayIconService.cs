using media_management_app.Models;

namespace media_management_app.Services;

public interface ITrayIconService : IDisposable
{
    bool IsInitialized { get; }

    void Initialize(MainWindow window);

    void HideToTray();

    void RestoreFromTray();

    void RequestShutdown();

    void ShowAutoTrackRunCompleted(AutoTrackRunResult result);
}
