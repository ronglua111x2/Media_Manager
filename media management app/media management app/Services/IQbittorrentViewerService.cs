namespace media_management_app.Services;

public interface IQbittorrentViewerService
{
    bool IsOpen { get; }

    event EventHandler? IsOpenChanged;

    void ShowOrActivate();

    void Close(bool skipConfirm = false);
}
