namespace media_management_app.Models;

public sealed class AutoTrackSettings
{
    public bool Enabled { get; set; } = true;

    public int IntervalHours { get; set; } = 6;

    public DateTime? LastRunUtc { get; set; }

    public string? LastRunSummary { get; set; }
}
