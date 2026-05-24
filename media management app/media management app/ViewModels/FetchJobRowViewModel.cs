using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed class FetchJobRowViewModel
{
    public FetchJobRowViewModel(FetchJob job)
    {
        Job = job;
    }

    public FetchJob Job { get; }

    public long Id => Job.Id;

    public string ShowTitle => Job.ShowTitle;

    public string Status => Job.Status.ToString();

    public string StatusIcon => Job.Status switch
    {
        Common.FetchJobStatus.Running => ">>",
        Common.FetchJobStatus.Pending => "...",
        Common.FetchJobStatus.Completed => "OK",
        Common.FetchJobStatus.Failed => "ERR",
        Common.FetchJobStatus.Canceled => "X",
        _ => string.Empty
    };

    public string Progress => Job.ProgressDisplay;

    public string ErrorSummary => Job.ErrorSummary ?? string.Empty;
}
