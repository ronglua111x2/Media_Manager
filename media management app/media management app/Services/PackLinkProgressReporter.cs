using media_management_app.Models;

namespace media_management_app.Services;

public static class PackLinkProgressReporter
{
    public static void Report(
        IProgress<PackLinkProgressUpdate>? progress,
        PackLinkProgressStep step,
        PackLinkProgressStatus status,
        string message,
        string? detail = null)
    {
        progress?.Report(new PackLinkProgressUpdate
        {
            Step = step,
            Status = status,
            Message = message,
            Detail = detail
        });
    }
}
