using media_management_app.Common;

namespace media_management_app.Models;

public sealed class UiSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Light;

    public NewsEpisodeSortMode NewsEpisodeSortMode { get; set; } = NewsEpisodeSortMode.AirDateDesc;

    public MediaCardSortMode LibraryMediaSortMode { get; set; } = MediaCardSortMode.DateAddedDesc;

    public string LibraryMediaSearchQuery { get; set; } = string.Empty;

    /// <summary>Null means "All statuses".</summary>
    public UserWatchStatus? LibraryWatchStatusFilter { get; set; }
}
