using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace media_management_app.ViewModels;

public sealed partial class LibraryViewModel
{
    [ObservableProperty]
    private bool isRatingChartVisible;

    [ObservableProperty]
    private EpisodeRatingViewKind episodeRatingViewKind = EpisodeRatingViewKind.Chart;

    [ObservableProperty]
    private SeasonListViewKind seasonListViewKind = SeasonListViewKind.Availability;

    [ObservableProperty]
    private int selectedRatingSeasonNumber;

    [ObservableProperty]
    private string selectedSeasonLabel = string.Empty;

    [ObservableProperty]
    private string selectedSeasonAverageValue = string.Empty;

    [ObservableProperty]
    private bool hasSeasonAverage;

    [ObservableProperty]
    private double averageGuideMargin;

    [ObservableProperty]
    private bool hasRatingSeasonEpisodes;

    public ObservableCollection<EpisodeRatingSeasonTabViewModel> RatingSeasonTabs { get; } = [];

    public ObservableCollection<EpisodeRatingCellViewModel> RatingCells { get; } = [];

    public IReadOnlyList<EpisodeRatingBandDefinition> RatingBandLegend { get; } = EpisodeRatingBandCatalog.LegendEntries;

    public bool ShowEpisodeRatingChartSection => IsSelectedShow;

    public bool ShowEpisodeRatingBody => IsRatingChartVisible;

    public bool ShowImdbChart =>
        IsRatingChartVisible && EpisodeRatingViewKind == EpisodeRatingViewKind.Chart && HasRatingSeasonEpisodes;

    public bool ShowHeatmap =>
        IsRatingChartVisible && EpisodeRatingViewKind == EpisodeRatingViewKind.Heatmap && HasRatingSeasonEpisodes;

    public bool IsChartView => EpisodeRatingViewKind == EpisodeRatingViewKind.Chart;

    public bool IsHeatmapView => EpisodeRatingViewKind == EpisodeRatingViewKind.Heatmap;

    public bool IsSeasonAvailabilityView => SeasonListViewKind == SeasonListViewKind.Availability;

    public bool IsSeasonRatingView => SeasonListViewKind == SeasonListViewKind.Rating;

    public bool CanMoveRatingSeasonPrevious => CanGoToPreviousRatingSeason();

    public bool CanMoveRatingSeasonNext => CanGoToNextRatingSeason();

    private bool CanGoToPreviousRatingSeason()
    {
        var index = RatingSeasonTabs.ToList().FindIndex(tab => tab.SeasonNumber == SelectedRatingSeasonNumber);
        return RatingSeasonTabs.Count > 1 && index > 0;
    }

    private bool CanGoToNextRatingSeason()
    {
        var index = RatingSeasonTabs.ToList().FindIndex(tab => tab.SeasonNumber == SelectedRatingSeasonNumber);
        return index >= 0 && index < RatingSeasonTabs.Count - 1;
    }

    [RelayCommand]
    private void ToggleRatingChart()
    {
        IsRatingChartVisible = !IsRatingChartVisible;
        if (IsRatingChartVisible)
        {
            RebuildRatingChart();
        }

        NotifyRatingChartVisibility();
    }

    [RelayCommand]
    private void SetEpisodeRatingChartView()
    {
        EpisodeRatingViewKind = EpisodeRatingViewKind.Chart;
    }

    [RelayCommand]
    private void SetEpisodeRatingHeatmapView()
    {
        EpisodeRatingViewKind = EpisodeRatingViewKind.Heatmap;
    }

    [RelayCommand]
    private void SetSeasonAvailabilityView()
    {
        SeasonListViewKind = SeasonListViewKind.Availability;
    }

    [RelayCommand]
    private void SetSeasonRatingView()
    {
        SeasonListViewKind = SeasonListViewKind.Rating;
    }

    [RelayCommand]
    private void SelectRatingSeason(EpisodeRatingSeasonTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        SelectedRatingSeasonNumber = tab.SeasonNumber;
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousRatingSeason))]
    private void MoveRatingSeasonPrevious() => MoveRatingSeasonBy(-1);

    [RelayCommand(CanExecute = nameof(CanGoToNextRatingSeason))]
    private void MoveRatingSeasonNext() => MoveRatingSeasonBy(1);

    private void MoveRatingSeasonBy(int delta)
    {
        var tabs = RatingSeasonTabs.ToList();
        var index = tabs.FindIndex(tab => tab.SeasonNumber == SelectedRatingSeasonNumber);
        var next = index + delta;
        if (next < 0 || next >= tabs.Count)
        {
            return;
        }

        SelectedRatingSeasonNumber = tabs[next].SeasonNumber;
    }

    [RelayCommand]
    private void SelectRatingCell(EpisodeRatingCellViewModel? cell)
    {
        if (cell is null)
        {
            return;
        }

        foreach (var item in RatingCells)
        {
            item.IsSelected = item.EpisodeNumber == cell.EpisodeNumber;
        }
    }

    [RelayCommand]
    private void ToggleEpisodeNotes(LibraryEpisodeRowViewModel? row)
    {
        if (row is null || !row.CanRateEpisode)
        {
            return;
        }

        if (row.IsNotesPanelOpen)
        {
            PersistEpisodeRatingAndThought(row);
            row.IsNotesPanelOpen = false;
            return;
        }

        CloseOpenEpisodeEditors(except: row);
        row.IsNotesPanelOpen = true;
    }

    [RelayCommand]
    private void IncrementEpisodeRating(LibraryEpisodeRowViewModel? row)
    {
        if (row is null || !row.CanRateEpisode)
        {
            return;
        }

        var next = row.UserRating is { } current
            ? current + RatingStep
            : RatingStep;
        row.UserRating = ClampRating(next);
        PersistEpisodeRatingAndThought(row);
    }

    [RelayCommand]
    private void DecrementEpisodeRating(LibraryEpisodeRowViewModel? row)
    {
        if (row is null || !row.CanRateEpisode || !row.HasRating)
        {
            return;
        }

        row.UserRating = ClampRating(row.UserRating!.Value - RatingStep);
        PersistEpisodeRatingAndThought(row);
    }

    [RelayCommand]
    private void CommitEpisodeRating(LibraryEpisodeRowViewModel? row)
    {
        if (row is null || !row.CanRateEpisode)
        {
            return;
        }

        PersistEpisodeRatingAndThought(row);
    }

    [RelayCommand]
    private void CommitEpisodeThought(LibraryEpisodeRowViewModel? row)
    {
        if (row is null || !row.CanRateEpisode)
        {
            return;
        }

        row.Thought = NormalizeThought(row.Thought);
        PersistEpisodeRatingAndThought(row);
    }

    private void HandleSelectedShowForRatingChart()
    {
        RefreshRatingSeasonTabs();
        if (IsRatingChartVisible)
        {
            RebuildRatingChart();
        }

        OnPropertyChanged(nameof(ShowEpisodeRatingChartSection));
        NotifyRatingChartVisibility();
    }

    partial void OnIsRatingChartVisibleChanged(bool value)
    {
        if (value)
        {
            RebuildRatingChart();
        }

        NotifyRatingChartVisibility();
    }

    partial void OnEpisodeRatingViewKindChanged(EpisodeRatingViewKind value)
    {
        NotifyRatingChartVisibility();
    }

    partial void OnSeasonListViewKindChanged(SeasonListViewKind value)
    {
        CloseOpenEpisodeEditors(except: null);
        OnPropertyChanged(nameof(IsSeasonAvailabilityView));
        OnPropertyChanged(nameof(IsSeasonRatingView));
    }

    private void CloseOpenEpisodeEditors(LibraryEpisodeRowViewModel? except)
    {
        if (SelectedShow is null)
        {
            return;
        }

        foreach (var episode in SelectedShow.Seasons.SelectMany(season => season.Episodes))
        {
            if (!episode.IsNotesPanelOpen || ReferenceEquals(episode, except))
            {
                continue;
            }

            PersistEpisodeRatingAndThought(episode);
            episode.IsNotesPanelOpen = false;
        }
    }

    partial void OnSelectedRatingSeasonNumberChanged(int value)
    {
        foreach (var tab in RatingSeasonTabs)
        {
            tab.IsSelected = tab.SeasonNumber == value;
        }

        MoveRatingSeasonPreviousCommand.NotifyCanExecuteChanged();
        MoveRatingSeasonNextCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanMoveRatingSeasonPrevious));
        OnPropertyChanged(nameof(CanMoveRatingSeasonNext));
        if (IsRatingChartVisible)
        {
            RebuildRatingChart();
        }
    }

    private void PersistEpisodeRatingAndThought(LibraryEpisodeRowViewModel row)
    {
        var rating = row.UserRating;
        if (rating.HasValue)
        {
            rating = ClampRating(rating.Value);
            row.UserRating = rating;
        }

        var thought = NormalizeThought(row.Thought);
        row.Thought = thought;
        var thoughtForDb = string.IsNullOrWhiteSpace(thought) ? null : thought;

        _trackedShowService.UpdateEpisodeRating(row.Id, rating, thoughtForDb);
        StatusMessage = rating.HasValue
            ? $"Episode {row.EpisodeCode} rating/thought updated: {rating.Value:0.0}."
            : $"Episode {row.EpisodeCode} rating cleared.";

        SelectedShow?.Seasons.FirstOrDefault(season => season.SeasonNumber == row.SeasonNumber)?.NotifySeasonChanged();

        if (IsRatingChartVisible)
        {
            RebuildRatingChart();
        }
    }

    private void RefreshRatingSeasonTabs()
    {
        var previous = SelectedRatingSeasonNumber;
        RatingSeasonTabs.Clear();
        if (SelectedShow is null)
        {
            SelectedRatingSeasonNumber = 0;
            RatingCells.Clear();
            NotifySeasonNav();
            return;
        }

        foreach (var season in SelectedShow.Seasons.Where(season => season.Episodes.Any(episode => episode.CanRateEpisode)))
        {
            RatingSeasonTabs.Add(new EpisodeRatingSeasonTabViewModel(season.SeasonNumber));
        }

        if (RatingSeasonTabs.Count == 0)
        {
            SelectedRatingSeasonNumber = 0;
            NotifySeasonNav();
            return;
        }

        if (RatingSeasonTabs.All(tab => tab.SeasonNumber != previous))
        {
            previous = RatingSeasonTabs[0].SeasonNumber;
        }

        SelectedRatingSeasonNumber = previous;
        foreach (var tab in RatingSeasonTabs)
        {
            tab.IsSelected = tab.SeasonNumber == SelectedRatingSeasonNumber;
        }

        NotifySeasonNav();
    }

    private void RebuildRatingChart()
    {
        RatingCells.Clear();
        HasSeasonAverage = false;
        HasRatingSeasonEpisodes = false;
        SelectedSeasonLabel = string.Empty;
        SelectedSeasonAverageValue = string.Empty;
        AverageGuideMargin = EpisodeRatingCellViewModel.PlotTrackHeight;

        if (SelectedShow is null)
        {
            NotifyRatingChartVisibility();
            return;
        }

        var season = SelectedShow.Seasons.FirstOrDefault(item => item.SeasonNumber == SelectedRatingSeasonNumber);
        var episodes = season?.Episodes
            .Where(episode => episode.CanRateEpisode)
            .OrderBy(episode => episode.EpisodeNumber)
            .ToList() ?? [];

        HasRatingSeasonEpisodes = episodes.Count > 0;
        foreach (var episode in episodes)
        {
            RatingCells.Add(new EpisodeRatingCellViewModel(episode.EpisodeNumber, episode.UserRating));
        }

        SelectedSeasonLabel = $"S{SelectedRatingSeasonNumber} Episodes";
        var rated = episodes.Where(episode => episode.HasRating).Select(episode => episode.UserRating!.Value).ToList();
        if (rated.Count == 0)
        {
            SelectedSeasonAverageValue = string.Empty;
        }
        else
        {
            var average = rated.Average();
            HasSeasonAverage = true;
            AverageGuideMargin = EpisodeRatingCellViewModel.AverageGuideOffset(average);
            SelectedSeasonAverageValue = average.ToString("0.0", CultureInfo.InvariantCulture);
        }

        NotifyRatingChartVisibility();
    }

    private void NotifySeasonNav()
    {
        MoveRatingSeasonPreviousCommand.NotifyCanExecuteChanged();
        MoveRatingSeasonNextCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanMoveRatingSeasonPrevious));
        OnPropertyChanged(nameof(CanMoveRatingSeasonNext));
    }

    private void NotifyRatingChartVisibility()
    {
        OnPropertyChanged(nameof(ShowEpisodeRatingChartSection));
        OnPropertyChanged(nameof(ShowEpisodeRatingBody));
        OnPropertyChanged(nameof(ShowImdbChart));
        OnPropertyChanged(nameof(ShowHeatmap));
        OnPropertyChanged(nameof(IsChartView));
        OnPropertyChanged(nameof(IsHeatmapView));
    }
}
