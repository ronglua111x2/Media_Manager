using System.Threading;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface IAutoTrackSchedulerService : IDisposable
{
    event EventHandler<AutoTrackRunResult>? RunCompleted;

    void Start();
}
