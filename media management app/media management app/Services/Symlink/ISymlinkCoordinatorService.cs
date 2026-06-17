using media_management_app.Models;

namespace media_management_app.Services.Symlink;

public interface ISymlinkCoordinatorService : IDisposable
{
    void Start();

    Task<SymlinkSyncResult> SyncNowAsync(CancellationToken cancellationToken = default);
}
