using media_management_app.Models;

namespace media_management_app.Services;

public interface IJellyfinClient
{
    Task<string> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task ReportMediaUpdatedAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JellyfinScheduledTaskInfo>> GetScheduledTasksAsync(CancellationToken cancellationToken = default);
}
