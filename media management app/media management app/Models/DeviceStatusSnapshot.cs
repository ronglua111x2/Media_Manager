using media_management_app.ViewModels;

namespace media_management_app.Models;

public sealed class DeviceStatusSnapshot
{
    public IReadOnlyList<StorageStatusViewModel> DriveStatuses { get; init; } = [];

    public DependencyStatusInfo Qbittorrent { get; init; } = new()
    {
        Name = "qBittorrent",
        StatusText = "unknown",
        Detail = "qBittorrent status unknown"
    };

    public DependencyStatusInfo Warp { get; init; } = new()
    {
        Name = "WARP",
        StatusText = "unknown",
        Detail = "WARP status unknown"
    };

    public DependencyStatusInfo Jellyfin { get; init; } = new()
    {
        Name = "Jellyfin",
        StatusText = "unknown",
        Detail = "Jellyfin status unknown"
    };

    public DependencyStatusInfo GoogleDrive { get; init; } = new()
    {
        Name = "Google Drive",
        StatusText = "unknown",
        Detail = "Google Drive status unknown"
    };

    public bool IsBackupRunning { get; init; }

    public string JobStatus { get; init; } = "Idle";

    public bool IsJobActive { get; init; }

    public bool HasLowSpace { get; init; }
}
