using media_management_app.Models;

namespace media_management_app.Services;

public interface ITorrentAddDiskAssignmentService
{
    TorrentAddDiskPlan BuildPlan(
        IReadOnlyList<TorrentCartOrder> orders,
        Func<long, int, string?> getSeasonDownloadFolder);

    void AutoAssign(TorrentAddDiskPlan plan);

    void Recalculate(TorrentAddDiskPlan plan);
}
