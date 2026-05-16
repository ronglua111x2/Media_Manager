using media_management_app.Common;

namespace media_management_app.Models;

public sealed class SourceItem
{
    public long Id { get; set; }

    public string SourceRootFolder { get; set; } = string.Empty;

    public string ParentFolder { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ScanText { get; set; } = string.Empty;

    public MediaKind MediaKind { get; set; } = MediaKind.Unknown;

    public string? ShowTitle { get; set; }

    public string? MovieTitle { get; set; }

    public int? MovieYear { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public string? EpisodeTitle { get; set; }

    public ItemState State { get; set; } = ItemState.Discovered;

    public string? Notes { get; set; }

    public string? LinkedPath { get; set; }

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
