using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class LibraryShowDetailViewModel : ObservableObject
{
    public LibraryShowDetailViewModel(
        TrackedShow show,
        IEnumerable<LibrarySeasonViewModel> seasons,
        int hiddenSeasonCount)
    {
        Id = show.Id;
        TmdbId = show.TmdbId;
        Title = show.DisplayTitle;
        Overview = show.Overview ?? string.Empty;
        PosterPath = show.PosterPath;
        HiddenSeasonCount = hiddenSeasonCount;
        SeriesStatus = show.SeriesStatus;
        SeriesStatusLabel = show.SeriesStatusLabel;
        AlternativeTitleChips = BuildAlternativeTitleChips(show.Id, isMovie: false, show.AlternativeTitles, show.ExcludedFromSearchAlternativeTitles);
        var seasonList = seasons.ToList();
        TotalEpisodes = seasonList.Sum(season => season.TotalEpisodes);
        AvailableEpisodes = seasonList.Sum(season => season.AvailableEpisodes);
        PreferencesSummary =
            $"Quality {show.PreferredQuality} | Audio {(string.IsNullOrWhiteSpace(show.PreferredAudioCodec) ? "Any" : show.PreferredAudioCodec)} | Min seeders {show.MinimumSeeders}";
        IsAutoTracked = show.IsAutoTracked;
        AutoTrackCheckpointLabel = show.AutoTrackCheckpointLabel;
        Seasons = new ObservableCollection<LibrarySeasonViewModel>(seasonList);
    }

    public long Id { get; }

    public int TmdbId { get; }

    public string Title { get; }

    public string Overview { get; }

    public string? PosterPath { get; }

    public int TotalEpisodes { get; }

    public int AvailableEpisodes { get; }

    public string PreferencesSummary { get; }

    public ObservableCollection<AlternativeTitleChipViewModel> AlternativeTitleChips { get; }

    public bool HasAlternativeTitles => AlternativeTitleChips.Count > 0;

    public bool IsAutoTracked { get; }

    public string AutoTrackCheckpointLabel { get; }

    public int HiddenSeasonCount { get; }

    public ShowSeriesStatus SeriesStatus { get; }

    public string SeriesStatusLabel { get; }

    public bool HasHiddenSeasons => HiddenSeasonCount > 0;

    public string Stats => $"{AvailableEpisodes}/{TotalEpisodes} available | {Seasons.Count} season(s)";

    public string TmdbPageUrl => $"https://www.themoviedb.org/tv/{TmdbId}";

    public string PosterUrl => string.IsNullOrWhiteSpace(PosterPath)
        ? string.Empty
        : $"https://image.tmdb.org/t/p/w342{PosterPath}";

    public ObservableCollection<LibrarySeasonViewModel> Seasons { get; }

    internal static ObservableCollection<AlternativeTitleChipViewModel> BuildAlternativeTitleChips(
        long mediaId,
        bool isMovie,
        IReadOnlyList<string> alternativeTitles,
        IReadOnlyList<string> excludedFromSearchAlternativeTitles)
    {
        var excluded = new HashSet<string>(excludedFromSearchAlternativeTitles, StringComparer.OrdinalIgnoreCase);
        return new ObservableCollection<AlternativeTitleChipViewModel>(
            alternativeTitles.Select(title => new AlternativeTitleChipViewModel(
                mediaId,
                isMovie,
                title,
                excluded.Contains(title))));
    }
}
