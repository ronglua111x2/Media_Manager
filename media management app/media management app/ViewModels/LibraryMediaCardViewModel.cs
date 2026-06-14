using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;

namespace media_management_app.ViewModels;

public partial class LibraryMediaCardViewModel : ObservableObject
{
    public long Id { get; init; }

    public int TmdbId { get; init; }

    public string Title { get; init; } = string.Empty;

    public MediaKind MediaKind { get; init; }

    public int? Year { get; init; }

    public DateTime CreatedUtc { get; init; }

    public int AvailableCount { get; init; }

    public int TotalCount { get; init; }

    public string? Overview { get; init; }

    public string? PosterPath { get; init; }

    public string TypeLabel => MediaKind == MediaKind.Movie ? "Movie" : "Show";

    public string? SeriesStatusLabel { get; init; }

    public bool HasSeriesStatusLabel => IsShow && !string.IsNullOrWhiteSpace(SeriesStatusLabel) && SeriesStatusLabel != "Unknown";

    public string YearLabel => Year?.ToString() ?? "Unknown";

    public string AddedLabel => CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd");

    public string ProgressLabel => MediaKind == MediaKind.Movie
        ? AvailableCount > 0 ? "Available" : "Not in library"
        : $"{AvailableCount}/{TotalCount} available";

    public int OrderCount { get; init; }

    public bool HasOrders => OrderCount > 0;

    public string OrderCountLabel => OrderCount switch
    {
        0 => string.Empty,
        1 => "1 order",
        _ => $"{OrderCount} orders"
    };

    public string PosterUrl => string.IsNullOrWhiteSpace(PosterPath)
        ? string.Empty
        : $"https://image.tmdb.org/t/p/w342{PosterPath}";

    public bool IsShow => MediaKind == MediaKind.TvEpisode;

    public bool IsMovie => MediaKind == MediaKind.Movie;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private ImageSource? posterImage;
}
