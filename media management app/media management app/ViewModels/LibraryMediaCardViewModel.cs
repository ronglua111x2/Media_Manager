using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public partial class LibraryMediaCardViewModel : ObservableObject, IMediaCardSortable
{
    public long Id { get; init; }

    public int TmdbId { get; init; }

    public string Title { get; init; } = string.Empty;

    public MediaKind MediaKind { get; init; }

    public int? Year { get; init; }

    public DateTime CreatedUtc { get; init; }

    public int AvailableCount { get; init; }

    public int TotalCount { get; init; }

    /// <summary>Watch progress denominator (max planned/synced). Distinct from disk TotalCount.</summary>
    public int WatchTotalCount { get; init; }

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
    [NotifyPropertyChangedFor(nameof(WatchStatusLabel))]
    [NotifyPropertyChangedFor(nameof(HasWatchStatusLabel))]
    [NotifyPropertyChangedFor(nameof(WatchedProgressLabel))]
    [NotifyPropertyChangedFor(nameof(HasWatchedProgressLabel))]
    [NotifyPropertyChangedFor(nameof(HasWatchRow))]
    private UserWatchStatus watchStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchedProgressLabel))]
    [NotifyPropertyChangedFor(nameof(HasWatchedProgressLabel))]
    [NotifyPropertyChangedFor(nameof(HasWatchRow))]
    private int watchedEpisodes;

    public string WatchStatusLabel => TrackedShow.FormatWatchStatusLabel(WatchStatus);

    public bool HasWatchStatusLabel => WatchStatus != UserWatchStatus.None;

    public string WatchedProgressLabel =>
        IsShow && (WatchStatus != UserWatchStatus.None || WatchedEpisodes > 0)
            ? $"{WatchedEpisodes}/{WatchTotalCount}"
            : string.Empty;

    public bool HasWatchedProgressLabel => !string.IsNullOrWhiteSpace(WatchedProgressLabel);

    public bool HasWatchRow => HasWatchStatusLabel || HasWatchedProgressLabel;

    public void ApplyWatchProgress(UserWatchStatus status, int watched)
    {
        WatchStatus = status;
        WatchedEpisodes = watched;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRating))]
    [NotifyPropertyChangedFor(nameof(RatingLabel))]
    private double? rating;

    public bool HasRating => Rating is > 0;

    public string RatingLabel => Rating?.ToString("0.0") ?? string.Empty;

    public void ApplyRating(double? rating) => Rating = rating;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private ImageSource? posterImage;
}
