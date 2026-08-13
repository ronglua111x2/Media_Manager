namespace media_management_app.Services;

public interface IJellyfinViewerService
{
    bool IsOpen { get; }

    event EventHandler? IsOpenChanged;

    void ShowOrActivate();

    void Close(bool skipConfirm = false);
}
