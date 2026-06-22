using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using System.Windows.Media;

namespace media_management_app.Models;

public sealed partial class TmdbUnifiedSearchResult : ObservableObject
{
    public int TmdbId { get; init; }

    public string Title { get; init; } = string.Empty;

    public MediaKind MediaKind { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(YearLabel))]
    [NotifyPropertyChangedFor(nameof(StatLabel))]
    [NotifyPropertyChangedFor(nameof(YearSortValue))]
    private int? year;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlternativeTitles))]
    private IReadOnlyList<string> alternativeTitles = [];

    public bool HasAlternativeTitles => AlternativeTitles.Count > 0;

    [ObservableProperty]
    private string? overview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PosterUrl))]
    private string? posterPath;

    [ObservableProperty]
    private ImageSource? posterImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatLabel))]
    [NotifyPropertyChangedFor(nameof(StatSortValue))]
    private int episodeCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatLabel))]
    [NotifyPropertyChangedFor(nameof(StatSortValue))]
    private int seasonCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatLabel))]
    [NotifyPropertyChangedFor(nameof(StatSortValue))]
    private int? runtimeMinutes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LibraryStatus))]
    [NotifyPropertyChangedFor(nameof(StatusSortValue))]
    private bool isAlreadyAdded;

    [ObservableProperty]
    private string? selectedRecipeId;

    [ObservableProperty]
    private bool isDetailsLoaded;

    public string TypeLabel => MediaKind == MediaKind.Movie ? "Movie" : "Show";

    public string YearLabel => Year?.ToString() ?? "Unknown";

    public string PosterUrl => string.IsNullOrWhiteSpace(PosterPath)
        ? string.Empty
        : $"https://image.tmdb.org/t/p/w342{PosterPath}";

    public string StatLabel => MediaKind == MediaKind.Movie
        ? RuntimeMinutes is > 0 ? FormatRuntime(RuntimeMinutes.Value) : string.Empty
        : SeasonCount > 0 || EpisodeCount > 0
            ? $"{SeasonCount} season{Pluralize(SeasonCount)}, {EpisodeCount} episode{Pluralize(EpisodeCount)}"
            : "Show";

    public string LibraryStatus => IsAlreadyAdded ? "Added" : "New";

    public int TypeSortValue => MediaKind == MediaKind.Movie ? 0 : 1;

    public int YearSortValue => Year ?? int.MaxValue;

    public int StatSortValue => MediaKind == MediaKind.Movie
        ? RuntimeMinutes ?? 0
        : EpisodeCount;

    public int StatusSortValue => IsAlreadyAdded ? 1 : 0;

    public string MediaAccentBrushKey => MediaKind == MediaKind.Movie ? "AppBrushMovieAccent" : "AppBrushShowAccent";

    public string MediaChipBackgroundBrushKey => MediaKind == MediaKind.Movie ? "AppBrushMovieSoft" : "AppBrushShowSoft";

    public string StatusAccentBrushKey => IsAlreadyAdded ? "AppBrushAccent" : "AppBrushNewAccent";

    public string StatusChipBackgroundBrushKey => IsAlreadyAdded ? "AppBrushAccentSoft" : "AppBrushNewSoft";

    public TmdbShowSearchResult ToShowSearchResult()
    {
        return new TmdbShowSearchResult
        {
            TmdbId = TmdbId,
            Title = Title,
            FirstAirYear = Year,
            Overview = Overview,
            PosterPath = PosterPath,
            SeasonCount = SeasonCount,
            EpisodeCount = EpisodeCount
        };
    }

    public TmdbMovieSearchResult ToMovieSearchResult()
    {
        return new TmdbMovieSearchResult
        {
            TmdbId = TmdbId,
            Title = Title,
            ReleaseYear = Year,
            Overview = Overview,
            PosterPath = PosterPath
        };
    }

    private static string Pluralize(int count)
    {
        return count == 1 ? string.Empty : "s";
    }

    private static string FormatRuntime(int minutes)
    {
        var hours = minutes / 60;
        var remainingMinutes = minutes % 60;
        return hours <= 0 ? $"{remainingMinutes}m" : $"{hours}h {remainingMinutes:00}m";
    }
}
