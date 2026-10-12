using media_management_app.Models;

namespace media_management_app.Services;

public interface IAutoTrackSchedulerService : IDisposable
{
    event EventHandler<AutoTrackRunResult>? RunCompleted;

    void Start();

    /// <summary>
    /// Arms reconcile polling after successful torrent adds. Polls only while a pending
    /// download/link queue exists; disarms when the queue is empty.
    /// </summary>
    void RequestReconcileAfterAdds();
}
