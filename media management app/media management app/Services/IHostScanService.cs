using media_management_app.Models;

namespace media_management_app.Services;

public interface IHostScanService
{
    Task<IReadOnlyList<HostScanRow>> ScanAsync(
        HostScanOverrides? overrides = null,
        CancellationToken cancellationToken = default);
}
