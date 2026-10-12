namespace media_management_app.Services;

public interface IQbittorrentViewerService
{
    bool IsOpen { get; }

    event EventHandler? IsOpenChanged;

    void ShowOrActivate(object? chromeDataContext = null);

    void Close(bool skipConfirm = false);
}
