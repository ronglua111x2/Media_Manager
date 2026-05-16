namespace media_management_app.Models;

public sealed class LibraryItem
{
    public long Id { get; set; }

    public long SourceItemId { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public string ShowTitle { get; set; } = string.Empty;

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public string? EpisodeTitle { get; set; }

    public string OutputPath { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
