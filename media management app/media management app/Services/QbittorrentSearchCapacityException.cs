namespace media_management_app.Services;

public sealed class QbittorrentSearchCapacityException : InvalidOperationException
{
    public QbittorrentSearchCapacityException()
        : base("qBittorrent search capacity is full. Too many searches are already running.")
    {
    }
}
