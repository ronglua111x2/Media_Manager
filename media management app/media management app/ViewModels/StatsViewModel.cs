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
    private readonly HashSet<long> _expandedHeatmapShowIds = [];
    private string _heatmapFingerprint = string.Empty;
    private string _showStripFingerprint = string.Empty;
    private string _movieStripFingerprint = string.Empty;

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
        MovieStrip = new StatsPosterStripViewModel(card =>
            LoadPosterAsync(card, card.Card.PosterPath, card.MediaKind, card.Card.TmdbId));
        ShowStrip = new StatsPosterStripViewModel(card =>
            LoadPosterAsync(card, card.Card.PosterPath, card.MediaKind, card.Card.TmdbId));
    }

    public IReadOnlyList<EpisodeRatingBandDefinition> TitleBandLegend { get; }

    public ObservableCollection<WatchStatusStat> WatchStatusStats { get; } = [];

    public ObservableCollection<StatsShowRowViewModel> HeatmapRows { get; } = [];

    public StatsPosterStripViewModel ShowStrip { get; }

    public StatsPosterStripViewModel MovieStrip { get; }

    public ObservableCollection<EpisodeHallOfFameEntry> TopEpisodes { get; } = [];

    public ObservableCollection<EpisodeHallOfFameEntry> BottomEpisodes { get; } = [];

    public ObservableCollection<EpisodeHallOfFameEntry> TopSpecials { get; } = [];

    public ObservableCollection<EpisodeHallOfFameEntry> BottomSpecials { get; } = [];

    public ObservableCollection<TitleEpisodeMismatch> Mismatches { get; } = [];

    public ObservableCollection<EmptyOpinion> EmptyOpinions { get; } = [];

    public ObservableCollection<PersonalRatingBandCount> TitleBands { get; } = [];

    public ObservableCollection<PersonalRatingBandCount> EpisodeBands { get; } = [];

    public ObservableCollection<StatsBillboardCardViewModel> BillboardHighlights { get; } = [];

    public ObservableCollection<StatsBillboardCardViewModel> BillboardRandom { get; } = [];

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

    [ObservableProperty]
    private bool hasBillboard;

    [ObservableProperty]
    private bool hasHighlightSlot;

    [ObservableProperty]
    private bool hasRandomSlot;

    [ObservableProperty]
    private bool isBillboardPaused;

    [ObservableProperty]
    private bool isBillboardActive;

    public override void OnNavigatedTo()
    {
        RefreshOverview();
        IsBillboardActive = true;
    }

    public override void OnNavigatedFrom()
    {
        IsBillboardActive = false;
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

        var heatmapFingerprint = HeatmapStripLayout.Fingerprint(overview.HeatmapRows);
        if (heatmapFingerprint != _heatmapFingerprint)
        {
            HeatmapRows.Clear();
            foreach (var row in overview.HeatmapRows)
            {
                var vm = new StatsShowRowViewModel(row, _expandedHeatmapShowIds);
                HeatmapRows.Add(vm);
                _ = LoadPosterAsync(vm, row.PosterPath, MediaKind.TvEpisode, row.TmdbId);
            }

            _heatmapFingerprint = heatmapFingerprint;
        }

        var showFingerprint = HeatmapStripLayout.TitleStripFingerprint(overview.RatedShows);
        if (showFingerprint != _showStripFingerprint)
        {
            ShowStrip.ReplaceSource(overview.RatedShows);
            _showStripFingerprint = showFingerprint;
        }

        var movieFingerprint = HeatmapStripLayout.TitleStripFingerprint(overview.RatedMovies);
        if (movieFingerprint != _movieStripFingerprint)
        {
            MovieStrip.ReplaceSource(overview.RatedMovies);
            _movieStripFingerprint = movieFingerprint;
        }

        ReplaceBillboardCards(BillboardHighlights, overview.BillboardHighlights);
        ReplaceBillboardCards(BillboardRandom, overview.BillboardRandom);

        HasWatchStatusStats = WatchStatusStats.Count > 0;
        HasHeatmapRows = HeatmapRows.Count > 0;
        HasShowStrip = ShowStrip.SourceCount > 0 || overview.UnratedShowCount > 0;
        HasMovieStrip = MovieStrip.SourceCount > 0 || overview.UnratedMovieCount > 0;
        HasTopEpisodes = TopEpisodes.Count > 0;
        HasBottomEpisodes = BottomEpisodes.Count > 0;
        HasTopSpecials = TopSpecials.Count > 0;
        HasBottomSpecials = BottomSpecials.Count > 0;
        HasMismatches = Mismatches.Count > 0;
        HasEmptyOpinions = EmptyOpinions.Count > 0;
        HasHighlightSlot = BillboardHighlights.Count > 0;
        HasRandomSlot = BillboardRandom.Count > 0;
        HasBillboard = HasHighlightSlot || HasRandomSlot;
    }

    [RelayCommand]
    private void ToggleBillboardPause() => IsBillboardPaused = !IsBillboardPaused;

    [RelayCommand]
    private void OpenShow(long showId) => OpenLibrary(MediaKind.TvEpisode, showId, seasonNumber: null);

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

    private void ReplaceBillboardCards(
        ObservableCollection<StatsBillboardCardViewModel> target,
        IReadOnlyList<StatsBillboardItem> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            var vm = new StatsBillboardCardViewModel(item);
            target.Add(vm);
            _ = LoadPosterAsync(vm, item.PosterPath, item.MediaKind, item.TmdbId);
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

    private async Task LoadPosterAsync(StatsBillboardCardViewModel card, string? posterPath, MediaKind kind, int tmdbId)
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
    private readonly HashSet<long> _expandedIds;

    public StatsShowRowViewModel(ShowHeatmapRow row, HashSet<long> expandedIds)
    {
        Row = row;
        _expandedIds = expandedIds;
        ShowRatingText = row.ShowRating is { } rating
            ? rating.ToString("0.0", CultureInfo.InvariantCulture)
            : "—";
        CoverageText = row.EpisodeCount == 0
            ? string.Empty
            : $"{row.RatedEpisodeCount}/{row.EpisodeCount} rated";
        HasSeasonDivider = row.Seasons.Count > 0 && row.ExtraSeasons.Count > 0;
        HasExtraSeasons = row.ExtraSeasons.Count > 0;
        PreviewSeason = row.Seasons.Count > 0 ? row.Seasons[0] : row.ExtraSeasons.FirstOrDefault();
        HasPreviewSeason = PreviewSeason is not null;
        PreviewIsExtra = row.Seasons.Count == 0 && PreviewSeason is not null;
        HasMoreSeasonGroups = row.Seasons.Count + row.ExtraSeasons.Count > 1;
        IsExpanded = expandedIds.Contains(row.ShowId);
        HasRealizedExpanded = IsExpanded;
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

    public bool HasPreviewSeason { get; }

    public bool PreviewIsExtra { get; }

    public bool HasMoreSeasonGroups { get; }

    public HeatmapSeasonGroup? PreviewSeason { get; }

    public IReadOnlyList<HeatmapSeasonGroup> Seasons => Row.Seasons;

    public IReadOnlyList<HeatmapSeasonGroup> ExtraSeasons => Row.ExtraSeasons;

    public bool CanExpand => IsExpanded || HasMoreSeasonGroups || IsPreviewTruncated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExpand))]
    private bool isPreviewTruncated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExpand))]
    private bool isExpanded;

    [ObservableProperty]
    private bool hasRealizedExpanded;

    [ObservableProperty]
    private bool isHeatmapLoading;

    [ObservableProperty]
    private ImageSource? posterImage;

    private int _heatmapLoadCount;

    public void BeginHeatmapLoad()
    {
        _heatmapLoadCount++;
        IsHeatmapLoading = true;
    }

    public void EndHeatmapLoad()
    {
        _heatmapLoadCount = Math.Max(0, _heatmapLoadCount - 1);
        IsHeatmapLoading = _heatmapLoadCount > 0;
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        if (!IsExpanded && !CanExpand)
        {
            return;
        }

        IsExpanded = !IsExpanded;
        if (IsExpanded)
        {
            HasRealizedExpanded = true;
            _expandedIds.Add(ShowId);
        }
        else
        {
            _expandedIds.Remove(ShowId);
        }
    }
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

public sealed partial class StatsBillboardCardViewModel : ObservableObject
{
    public StatsBillboardCardViewModel(StatsBillboardItem item)
    {
        Item = item;
        RatingText = item.Rating.ToString("0.0", CultureInfo.InvariantCulture);
        ThoughtDisplay = item.HasThought ? item.Thought!.Trim() : string.Empty;
    }

    public StatsBillboardItem Item { get; }

    public StatsBillboardKind Kind => Item.Kind;

    public MediaKind MediaKind => Item.MediaKind;

    public long MediaId => Item.MediaId;

    public int? SeasonNumber => Item.IsEpisode ? Item.SeasonNumber : null;

    public bool IsEpisode => Item.IsEpisode;

    public string IdentityKey => Item.IdentityKey;

    public string KindLabel => Item.KindLabel;

    public string Headline => Item.Headline;

    public string? Subtitle => Item.Subtitle;

    public bool HasSubtitle => Item.HasSubtitle;

    public EpisodeRatingBand Band => Item.Band;

    public string RatingText { get; }

    public bool HasThought => Item.HasThought;

    public string ThoughtDisplay { get; }

    [ObservableProperty]
    private ImageSource? posterImage;
}
