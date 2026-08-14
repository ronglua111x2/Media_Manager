using media_management_app.Common;

namespace media_management_app.ViewModels;

public sealed class FindAddMediaCardViewModel : IMediaCardSortable
{
    public long Id { get; init; }

    public int TmdbId { get; init; }

    public string Title { get; init; } = string.Empty;

    public MediaKind MediaKind { get; init; }

    public int? Year { get; init; }

    public DateTime CreatedUtc { get; init; }

    public double? Rating { get; init; }

    public int AvailableCount { get; init; }

    public int TotalCount { get; init; }

    public string TypeLabel => MediaKind == MediaKind.Movie ? "Movie" : "Show";

    public string YearLabel => Year?.ToString() ?? "Unknown";

    public string AddedLabel => CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd");

    public string ProgressLabel => MediaKind == MediaKind.Movie
        ? "Library item"
        : $"{AvailableCount}/{TotalCount} available";

    public string MediaAccentBrushKey => MediaKind == MediaKind.Movie ? "AppBrushMovieAccent" : "AppBrushShowAccent";

    public string MediaCardBackgroundBrushKey => MediaKind == MediaKind.Movie ? "AppBrushMovieSoft" : "AppBrushShowSoft";
}
