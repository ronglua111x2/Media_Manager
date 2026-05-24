using media_management_app.Common;

namespace media_management_app.Models;

public sealed class FetchJob
{
    public long Id { get; set; }

    public long ShowId { get; set; }

    public MediaKind TargetKind { get; set; } = MediaKind.TvEpisode;

    public string ShowTitle { get; set; } = string.Empty;

    public FetchJobStatus Status { get; set; } = FetchJobStatus.Pending;

    public int TotalEpisodes { get; set; }

    public int ProcessedEpisodes { get; set; }

    public string? ErrorSummary { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? StartedUtc { get; set; }

    public DateTime? FinishedUtc { get; set; }

    public string ProgressDisplay => TotalEpisodes <= 0 ? string.Empty : $"{ProcessedEpisodes}/{TotalEpisodes}";
}
