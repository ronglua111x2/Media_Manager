namespace media_management_app.Models;

public sealed class TrackedSeason
{
    public long Id { get; set; }

    public long ShowId { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeCount { get; set; }

    public string? DownloadFolder { get; set; }
}
