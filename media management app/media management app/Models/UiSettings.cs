using media_management_app.Common;

namespace media_management_app.Models;

public sealed class UiSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Light;

    public NewsEpisodeSortMode NewsEpisodeSortMode { get; set; } = NewsEpisodeSortMode.AirDateDesc;

    public NewsTrackedShowViewMode NewsTrackedShowViewMode { get; set; } = NewsTrackedShowViewMode.Full;

    public MediaCardSortMode LibraryMediaSortMode { get; set; } = MediaCardSortMode.DateAddedDesc;

    public string LibraryMediaSearchQuery { get; set; } = string.Empty;

    /// <summary>Null means "All statuses".</summary>
    public UserWatchStatus? LibraryWatchStatusFilter { get; set; }

    public long? LibrarySelectedMediaId { get; set; }

    public MediaKind? LibrarySelectedMediaKind { get; set; }

    public MediaCardSortMode TorrentMediaSortMode { get; set; } = MediaCardSortMode.DateAddedDesc;

    public string TorrentMediaSearchQuery { get; set; } = string.Empty;

    /// <summary>Null means "All statuses".</summary>
    public UserWatchStatus? TorrentWatchStatusFilter { get; set; }

    public long? TorrentSelectedMediaId { get; set; }

    public MediaKind? TorrentSelectedMediaKind { get; set; }
}
