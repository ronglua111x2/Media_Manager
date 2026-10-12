using media_management_app.Models;

namespace media_management_app.Services;

public interface IPackLinkCoordinatorService
{
    event EventHandler<PackReconcileResult>? PackReconciled;

    Task<PackReconcileResult> ReconcilePackAsync(
        long showId,
        int ownerSeasonNumber,
        PackLinkTrigger trigger,
        CancellationToken cancellationToken = default);

    Task TryAutoReconcilePackAsync(
        TrackedShow show,
        TrackedSeason season,
        AddedTorrentResult torrent,
        double previousProgress,
        TorrentReconciliationResult reconcileResult,
        CancellationToken cancellationToken = default);
}
