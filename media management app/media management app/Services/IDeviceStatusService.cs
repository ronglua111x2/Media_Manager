using media_management_app.Models;

namespace media_management_app.Services;

public interface IDeviceStatusService
{
    event EventHandler? StatusChanged;

    DeviceStatusSnapshot Current { get; }

    Task RefreshAsync(CancellationToken cancellationToken = default);

    void RefreshWarpOnly();
}
