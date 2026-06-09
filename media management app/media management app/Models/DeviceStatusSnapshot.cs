namespace media_management_app.Models;

public sealed class DeviceStatusSnapshot
{
    public string StorageSummary { get; init; } = "Storage: not configured";

    public string QbittorrentStatus { get; init; } = "qBittorrent: unknown";

    public string JobStatus { get; init; } = "Jobs: idle";

    public bool HasLowSpace { get; init; }

    public bool IsQbittorrentConnected { get; init; }
}
