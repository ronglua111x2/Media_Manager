using media_management_app.Models;

namespace media_management_app.Services;

public interface IAutoTrackService
{
    bool IsRunning { get; }

    bool IsTmdbDiscoveryRunning { get; }

    bool IsTorrentHuntRunning { get; }

    bool IsReconcileRunning { get; }

    Task<AutoTrackRunResult> RunAsync(CancellationToken cancellationToken = default);

    Task<AutoTrackRunResult> RunTmdbDiscoveryAsync(bool bypassAnchor = false, CancellationToken cancellationToken = default);

    Task<AutoTrackRunResult> RunTorrentHuntAsync(
        CancellationToken cancellationToken = default,
        bool resumeOnly = false,
        bool bypassSchedule = false);

    Task<AutoTrackRunResult> RunBackgroundReconcileAsync(CancellationToken cancellationToken = default);

    void RecordRunResult(AutoTrackRunResult result);
}
