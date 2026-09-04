using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class StatsViewModel : ViewModelBase
{
    private readonly IDatabaseService _databaseService;
    private readonly IPosterImageService _posterImageService;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly IWorkspaceNavigator _workspaceNavigator;

    public StatsViewModel(
        IDatabaseService databaseService,
        IPosterImageService posterImageService,
        LibraryViewModel libraryViewModel,
        IWorkspaceNavigator workspaceNavigator)
    {
        _databaseService = databaseService;
        _posterImageService = posterImageService;
        _libraryViewModel = libraryViewModel;
        _workspaceNavigator = workspaceNavigator;
        TitleBandLegend = EpisodeRatingBandCatalog.LegendEntries
            .Where(entry => entry.Band != EpisodeRatingBand.Unrated)
            .ToList();
    }

    public IReadOnlyList<EpisodeRatingBandDefinition> TitleBandLegend { get; }

    public ObservableCollection<WatchStatusStat> WatchStatusStats { get; } = [];

    public ObservableCollection<StatsShowRowViewModel> HeatmapRows { get; } = [];

    public ObservableCollection<StatsPosterCardViewModel> RatedShows { get; } = [];

    public ObservableCollection<StatsPosterCardViewModel> RatedMovies { get; } = [];

    public ObservableCollection<EpisodeHallOfFameEntry> TopEpisodes { get; } = [];

    public ObservableCollection<EpisodeHallOfFameEntry> BottomEpisodes { get; } = [];

    public ObservableCollection<EpisodeHallOfFameEntry> TopSpecials { get; } = [];

    public ObservableCollection<EpisodeHallOfFameEntry> BottomSpecials { get; } = [];

    public ObservableCollection<TitleEpisodeMismatch> Mismatches { get; } = [];

    public ObservableCollection<EmptyOpinion> EmptyOpinions { get; } = [];

    public ObservableCollection<PersonalRatingBandCount> TitleBands { get; } = [];

    public ObservableCollection<PersonalRatingBandCount> EpisodeBands { get; } = [];

    [ObservableProperty]
    private ISeries[] titleBandPieSeries = [];

    [ObservableProperty]
    private ISeries[] episodeBandPieSeries = [];

    [ObservableProperty]
    private bool hasTitleBandPie;

    [ObservableProperty]
    private bool hasEpisodeBandPie;

    [ObservableProperty]
    private bool hasLibrary;

    [ObservableProperty]
    private string showCountText = "0 shows";

    [ObservableProperty]
    private string movieCountText = "0 movies";

    [ObservableProperty]
    private string ratedShowText = "0 rated";

    [ObservableProperty]
    private string ratedMovieText = "0 rated";

    [ObservableProperty]
    private string ratedEpisodeText = "0 rated episodes";

    [ObservableProperty]
    private string meanShowText = "—";

    [ObservableProperty]
    private string meanMovieText = "—";

    [ObservableProperty]
    private string meanEpisodeText = "—";

    [ObservableProperty]
    private string unratedShowCaption = string.Empty;

    [ObservableProperty]
    private string unratedMovieCaption = string.Empty;

    [ObservableProperty]
    private bool hasWatchStatusStats;

    [ObservableProperty]
    private bool hasHeatmapRows;

    [ObservableProperty]
    private bool hasShowStrip;

    [ObservableProperty]
    private bool hasMovieStrip;

    [ObservableProperty]
    private bool hasTopEpisodes;

    [ObservableProperty]
    private bool hasBottomEpisodes;

    [ObservableProperty]
    private bool hasTopSpecials;

    [ObservableProperty]
    private bool hasBottomSpecials;

    [ObservableProperty]
    private bool hasMismatches;

    [ObservableProperty]
    private bool hasEmptyOpinions;

    public override void OnNavigatedTo()
    {
        RefreshOverview();
    }

    [RelayCommand]
    private void RefreshOverview()
    {
        var overview = PersonalRatingOverviewBuilder.Build(
            _databaseService.GetTrackedShows(),
            _databaseService.GetTrackedMovies(),
            _databaseService.GetAllEpisodeUserRatings());

        HasLibrary = overview.HasLibrary;
        ShowCountText = FormatCount(overview.ShowCount, "show", "shows");
        MovieCountText = FormatCount(overview.MovieCount, "movie", "movies");
        RatedShowText = $"{overview.RatedShowCount} rated";
        RatedMovieText = $"{overview.RatedMovieCount} rated";
        RatedEpisodeText = overview.EpisodeCount == 0
            ? "0 rated episodes"
            : $"{overview.RatedEpisodeCount}/{overview.EpisodeCount} episodes rated";
        MeanShowText = FormatMean(overview.MeanShowRating);
        MeanMovieText = FormatMean(overview.MeanMovieRating);
        MeanEpisodeText = FormatMean(overview.MeanEpisodeRating);
        UnratedShowCaption = overview.UnratedShowCount == 0
            ? string.Empty
            : FormatCount(overview.UnratedShowCount, "unrated show", "unrated shows");
        UnratedMovieCaption = overview.UnratedMovieCount == 0
            ? string.Empty
            : FormatCount(overview.UnratedMovieCount, "unrated movie", "unrated movies");

        Replace(WatchStatusStats, overview.WatchStatusStats);
        Replace(TitleBands, overview.TitleBands);
        Replace(EpisodeBands, overview.EpisodeBands);
        TitleBandPieSeries = StatsBandPieSeries.FromBands(TitleBands);
        EpisodeBandPieSeries = StatsBandPieSeries.FromBands(EpisodeBands);
        HasTitleBandPie = TitleBandPieSeries.Length > 0;
        HasEpisodeBandPie = EpisodeBandPieSeries.Length > 0;
        Replace(TopEpisodes, overview.TopEpisodes);
        Replace(BottomEpisodes, overview.BottomEpisodes);
        Replace(TopSpecials, overview.TopSpecials);
        Replace(BottomSpecials, overview.BottomSpecials);
        Replace(Mismatches, overview.Mismatches);
        Replace(EmptyOpinions, overview.EmptyOpinions);

        HeatmapRows.Clear();
        foreach (var row in overview.HeatmapRows)
        {
            var vm = new StatsShowRowViewModel(row);
            HeatmapRows.Add(vm);
            _ = LoadPosterAsync(vm, row.PosterPath, MediaKind.TvEpisode, row.TmdbId);
        }

        ReplacePosterCards(RatedShows, overview.RatedShows);
        ReplacePosterCards(RatedMovies, overview.RatedMovies);

        HasWatchStatusStats = WatchStatusStats.Count > 0;
        HasHeatmapRows = HeatmapRows.Count > 0;
        HasShowStrip = RatedShows.Count > 0 || overview.UnratedShowCount > 0;
        HasMovieStrip = RatedMovies.Count > 0 || overview.UnratedMovieCount > 0;
        HasTopEpisodes = TopEpisodes.Count > 0;
        HasBottomEpisodes = BottomEpisodes.Count > 0;
        HasTopSpecials = TopSpecials.Count > 0;
        HasBottomSpecials = BottomSpecials.Count > 0;
        HasMismatches = Mismatches.Count > 0;
        HasEmptyOpinions = EmptyOpinions.Count > 0;
    }

    [RelayCommand]
    private void OpenShow(long showId) => OpenLibrary(MediaKind.TvEpisode, showId, seasonNumber: null);

    [RelayCommand]
    private void OpenHeatmapCell(HeatmapEpisodeCell? cell)
    {
        if (cell is null)
        {
            return;
        }

        OpenLibrary(MediaKind.TvEpisode, cell.ShowId, cell.SeasonNumber);
    }

    [RelayCommand]
    private void OpenRatedTitle(StatsPosterCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        OpenLibrary(card.MediaKind, card.MediaId, seasonNumber: null);
    }

    [RelayCommand]
    private void OpenHallOfFame(EpisodeHallOfFameEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        OpenLibrary(MediaKind.TvEpisode, entry.ShowId, entry.SeasonNumber);
    }

    [RelayCommand]
    private void OpenMismatch(TitleEpisodeMismatch? mismatch)
    {
        if (mismatch is null)
        {
            return;
        }

        OpenLibrary(MediaKind.TvEpisode, mismatch.ShowId, seasonNumber: null);
    }

    [RelayCommand]
    private void OpenEmptyOpinion(EmptyOpinion? opinion)
    {
        if (opinion is null)
        {
            return;
        }

        OpenLibrary(opinion.MediaKind, opinion.MediaId, seasonNumber: null);
    }

    private void OpenLibrary(MediaKind kind, long id, int? seasonNumber)
    {
        _libraryViewModel.PrepareSelect(kind, id, seasonNumber);
        _workspaceNavigator.NavigateTo(AppWorkspaceKind.Library);
    }

    private void ReplacePosterCards(
        ObservableCollection<StatsPosterCardViewModel> target,
        IReadOnlyList<TitleRatingCard> source)
    {
        target.Clear();
        foreach (var card in source)
        {
            var vm = new StatsPosterCardViewModel(card);
            target.Add(vm);
            _ = LoadPosterAsync(vm, card.PosterPath, card.MediaKind, card.TmdbId);
        }
    }

    private async Task LoadPosterAsync(StatsShowRowViewModel row, string? posterPath, MediaKind kind, int tmdbId)
    {
        row.PosterImage = await _posterImageService.LoadAsync(posterPath, kind, tmdbId, width: 342);
    }

    private async Task LoadPosterAsync(StatsPosterCardViewModel card, string? posterPath, MediaKind kind, int tmdbId)
    {
        card.PosterImage = await _posterImageService.LoadAsync(posterPath, kind, tmdbId, width: 342);
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static string FormatCount(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural}";

    private static string FormatMean(double? value) =>
        value is { } mean
            ? mean.ToString("0.0", CultureInfo.InvariantCulture)
            : "—";
}

public sealed partial class StatsShowRowViewModel : ObservableObject
{
    public StatsShowRowViewModel(ShowHeatmapRow row)
    {
        Row = row;
        ShowRatingText = row.ShowRating is { } rating
            ? rating.ToString("0.0", CultureInfo.InvariantCulture)
            : "—";
        CoverageText = row.EpisodeCount == 0
            ? string.Empty
            : $"{row.RatedEpisodeCount}/{row.EpisodeCount} rated";
        HasSeasonDivider = row.Seasons.Count > 0 && row.ExtraSeasons.Count > 0;
        HasExtraSeasons = row.ExtraSeasons.Count > 0;
    }

    public ShowHeatmapRow Row { get; }

    public long ShowId => Row.ShowId;

    public string Title => Row.Title;

    public EpisodeRatingBand ShowBand => Row.ShowBand;

    public UserWatchStatus WatchStatus => Row.WatchStatus;

    public bool HasWatchStatus => Row.HasWatchStatus;

    public string WatchStatusLabel => Row.WatchStatusLabel;

    public string ShowRatingText { get; }

    public string CoverageText { get; }

    public bool HasSeasonDivider { get; }

    public bool HasExtraSeasons { get; }

    public IReadOnlyList<HeatmapSeasonGroup> Seasons => Row.Seasons;

    public IReadOnlyList<HeatmapSeasonGroup> ExtraSeasons => Row.ExtraSeasons;

    [ObservableProperty]
    private ImageSource? posterImage;
}

public sealed partial class StatsPosterCardViewModel : ObservableObject
{
    public StatsPosterCardViewModel(TitleRatingCard card)
    {
        Card = card;
        RatingText = card.Rating.ToString("0.0", CultureInfo.InvariantCulture);
    }

    public TitleRatingCard Card { get; }

    public MediaKind MediaKind => Card.MediaKind;

    public long MediaId => Card.MediaId;

    public string Title => Card.Title;

    public EpisodeRatingBand Band => Card.Band;

    public UserWatchStatus WatchStatus => Card.WatchStatus;

    public bool HasWatchStatus => Card.HasWatchStatus;

    public string WatchStatusLabel => Card.WatchStatusLabel;

    public string RatingText { get; }

    [ObservableProperty]
    private ImageSource? posterImage;
}
