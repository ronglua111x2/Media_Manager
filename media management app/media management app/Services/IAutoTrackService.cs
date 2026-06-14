using media_management_app.Models;

namespace media_management_app.Services;

public interface IAutoTrackService
{
    bool IsRunning { get; }

    Task<AutoTrackRunResult> RunAsync(CancellationToken cancellationToken = default);
}
