using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class NewsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITorrentCartService _torrentCartService;
    private readonly IPosterImageService _posterImageService;
    private readonly List<NewsEpisodeCardViewModel> _weekEpisodeSource = [];
    private bool _isRestoringUiState;

    public NewsViewModel(
        ISettingsService settingsService,
        ITrackedShowService trackedShowService,
        ITorrentCartService torrentCartService,
        IAutoTrackSchedulerService autoTrackSchedulerService,
        IPosterImageService posterImageService)
    {
        _settingsService = settingsService;
        _trackedShowService = trackedShowService;
        _torrentCartService = torrentCartService;
        _posterImageService = posterImageService;

        autoTrackSchedulerService.RunCompleted += (_, _) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(UpdateDashboard);
        };

        _isRestoringUiState = true;
        NewsSortMode = _settingsService.Current.Ui?.NewsEpisodeSortMode ?? NewsEpisodeSortMode.AirDateDesc;
        TrackedShowViewMode = _settingsService.Current.Ui?.NewsTrackedShowViewMode ?? NewsTrackedShowViewMode.Full;
        _isRestoringUiState = false;

        UpdateDashboard();
    }

    public ObservableCollection<NewsShowCardViewModel> TrackedShows { get; } = [];

    public ObservableCollection<NewsEpisodeCardViewModel> NewEpisodesThisWeek { get; } = [];

    [ObservableProperty]
    private string todaySummary = string.Empty;

    [ObservableProperty]
    private string newThisWeekTitle = "New This Week";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAirDateSortSelected))]
    [NotifyPropertyChangedFor(nameof(IsTrackedShowSortSelected))]
    [NotifyPropertyChangedFor(nameof(IsStatusSortSelected))]
    private NewsEpisodeSortMode newsSortMode = NewsEpisodeSortMode.AirDateDesc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFullViewSelected))]
    [NotifyPropertyChangedFor(nameof(IsScheduleOnlyViewSelected))]
    [NotifyPropertyChangedFor(nameof(IsAvailabilityOnlyViewSelected))]
    [NotifyPropertyChangedFor(nameof(IsTitleOnlyViewSelected))]
    [NotifyPropertyChangedFor(nameof(ShowSeriesStatus))]
    [NotifyPropertyChangedFor(nameof(ShowScheduleRow))]
    [NotifyPropertyChangedFor(nameof(ShowAvailabilityRow))]
    [NotifyPropertyChangedFor(nameof(TrackedShowPosterWidth))]
    [NotifyPropertyChangedFor(nameof(TrackedShowPosterHeight))]
    [NotifyPropertyChangedFor(nameof(TrackedShowCardPadding))]
    private NewsTrackedShowViewMode trackedShowViewMode = NewsTrackedShowViewMode.Full;

    public bool IsAirDateSortSelected => NewsSortMode == NewsEpisodeSortMode.AirDateDesc;

    public bool IsTrackedShowSortSelected => NewsSortMode == NewsEpisodeSortMode.TrackedShow;

    public bool IsStatusSortSelected => NewsSortMode == NewsEpisodeSortMode.Status;

    public bool IsFullViewSelected => TrackedShowViewMode == NewsTrackedShowViewMode.Full;

    public bool IsScheduleOnlyViewSelected => TrackedShowViewMode == NewsTrackedShowViewMode.ScheduleOnly;

    public bool IsAvailabilityOnlyViewSelected => TrackedShowViewMode == NewsTrackedShowViewMode.AvailabilityOnly;

    public bool IsTitleOnlyViewSelected => TrackedShowViewMode == NewsTrackedShowViewMode.TitleOnly;

    public bool ShowSeriesStatus =>
        TrackedShowViewMode is NewsTrackedShowViewMode.Full or NewsTrackedShowViewMode.TitleOnly;

    public bool ShowScheduleRow =>
        TrackedShowViewMode is NewsTrackedShowViewMode.Full or NewsTrackedShowViewMode.ScheduleOnly;

    public bool ShowAvailabilityRow =>
        TrackedShowViewMode is NewsTrackedShowViewMode.Full or NewsTrackedShowViewMode.AvailabilityOnly;

    public double TrackedShowPosterWidth => TrackedShowViewMode switch
    {
        NewsTrackedShowViewMode.TitleOnly => 40,
        NewsTrackedShowViewMode.ScheduleOnly or NewsTrackedShowViewMode.AvailabilityOnly => 56,
        _ => 72
    };

    public double TrackedShowPosterHeight => TrackedShowViewMode switch
    {
        NewsTrackedShowViewMode.TitleOnly => 60,
        NewsTrackedShowViewMode.ScheduleOnly or NewsTrackedShowViewMode.AvailabilityOnly => 84,
        _ => 108
    };

    public Thickness TrackedShowCardPadding => TrackedShowViewMode switch
    {
        NewsTrackedShowViewMode.TitleOnly => new Thickness(6),
        NewsTrackedShowViewMode.ScheduleOnly or NewsTrackedShowViewMode.AvailabilityOnly => new Thickness(8),
        _ => new Thickness(10)
    };

    [RelayCommand]
    private void SetNewsSortMode(NewsEpisodeSortMode mode)
    {
        NewsSortMode = mode;
    }

    [RelayCommand]
    private void SetTrackedShowViewMode(NewsTrackedShowViewMode mode)
    {
        TrackedShowViewMode = mode;
    }

    partial void OnNewsSortModeChanged(NewsEpisodeSortMode value)
    {
        ApplyNewsEpisodeSort();
        if (_isRestoringUiState)
        {
            return;
        }

        PersistNewsSortMode(value);
    }

    partial void OnTrackedShowViewModeChanged(NewsTrackedShowViewMode value)
    {
        if (_isRestoringUiState)
        {
            return;
        }

        PersistTrackedShowViewMode(value);
    }

    [RelayCommand(CanExecute = nameof(CanOpenJellyfin))]
    private void OpenJellyfin()
    {
        var baseUrl = GetJellyfinBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(baseUrl)
        {
            UseShellExecute = true
        });
    }

    private bool CanOpenJellyfin() => !string.IsNullOrWhiteSpace(GetJellyfinBaseUrl());

    private string GetJellyfinBaseUrl()
    {
        var baseUrl = _settingsService.Current.AutoTrack?.Jellyfin?.BaseUrl;
        return string.IsNullOrWhiteSpace(baseUrl)
            ? string.Empty
            : baseUrl.Trim().TrimEnd('/');
    }

    [RelayCommand]
    private void UpdateDashboard()
    {
        OpenJellyfinCommand.NotifyCanExecuteChanged();
        TrackedShows.Clear();
        _weekEpisodeSource.Clear();
        NewEpisodesThisWeek.Clear();

        TodaySummary = $"Today is {AppTimeZone.FormatLongDate(AppTimeZone.Now)}";

        var shows = _trackedShowService.GetAutoTrackedShows();
        var autoTrackSettings = _settingsService.Current.AutoTrack ?? new AutoTrackSettings();
        var weekStart = AppTimeZone.Today.AddDays(-7);
        var stillKeepSet = new List<(int ShowTmdbId, int Season, int Episode)>();

        foreach (var show in shows)
        {
            if (show.AutoTrackFromSeason is null || show.AutoTrackFromEpisode is null)
            {
                continue;
            }

            var episodes = _trackedShowService.GetEpisodes(show.Id);
            var pending = CountPendingEpisodes(show, episodes);
            var isNotAired = IsShowNotYetAired(
                episodes,
                show.AutoTrackFromSeason.Value,
                show.AutoTrackFromEpisode.Value);
            var airDay = ShowWeeklyAirDay.Infer(
                episodes,
                show.AutoTrackFromSeason,
                show.AutoTrackFromEpisode);
            var card = new NewsShowCardViewModel(
                show.Id,
                show.DisplayTitle,
                show.PosterPath,
                show.SeriesStatusLabel,
                pending,
                ShowWeeklyAirDay.FormatLabel(airDay),
                AutoTrackWeekAnchor.FormatEffectiveAnchor(show, autoTrackSettings),
                isNotAired);
            TrackedShows.Add(card);
            _ = LoadShowPosterAsync(card, show);

            foreach (var episode in episodes
                         .Where(episode => episode.AirDate is not null && episode.AirDate.Value.Date >= weekStart)
                         .Where(episode => IsAtOrAfterCheckpoint(
                             episode,
                             show.AutoTrackFromSeason.Value,
                             show.AutoTrackFromEpisode.Value)))
            {
                var episodeCard = new NewsEpisodeCardViewModel(show, episode);
                _weekEpisodeSource.Add(episodeCard);
                if (!string.IsNullOrWhiteSpace(episode.StillPath))
                {
                    stillKeepSet.Add((show.TmdbId, episode.SeasonNumber, episode.EpisodeNumber));
                }

                _ = LoadEpisodeStillAsync(episodeCard, show);
            }
        }

        ApplyNewsEpisodeSort();
        _posterImageService.DeleteStillsNotIn(stillKeepSet);
    }

    private void ApplyNewsEpisodeSort()
    {
        NewEpisodesThisWeek.Clear();
        foreach (var card in SortWeekEpisodes(_weekEpisodeSource, NewsSortMode))
        {
            NewEpisodesThisWeek.Add(card);
        }

        var count = NewEpisodesThisWeek.Count;
        NewThisWeekTitle = count > 0
            ? $"New This Week ({count})"
            : "New This Week";
    }

    private static IEnumerable<NewsEpisodeCardViewModel> SortWeekEpisodes(
        IReadOnlyList<NewsEpisodeCardViewModel> source,
        NewsEpisodeSortMode mode)
    {
        return mode switch
        {
            NewsEpisodeSortMode.TrackedShow => source
                .GroupBy(card => card.ShowId)
                .OrderByDescending(group => group.Max(card => card.AirDate ?? DateTime.MinValue))
                .ThenBy(group => group.First().ShowTitle, StringComparer.OrdinalIgnoreCase)
                .SelectMany(group => group
                    .OrderByDescending(card => card.AirDate)
                    .ThenBy(card => card.SeasonNumber)
                    .ThenBy(card => card.EpisodeNumber)),
            NewsEpisodeSortMode.Status => source
                .OrderBy(card => card.StatusSortRank)
                .ThenByDescending(card => card.AirDate)
                .ThenBy(card => card.ShowTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(card => card.SeasonNumber)
                .ThenBy(card => card.EpisodeNumber),
            _ => source
                .OrderByDescending(card => card.AirDate)
                .ThenBy(card => card.ShowTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(card => card.SeasonNumber)
                .ThenBy(card => card.EpisodeNumber)
        };
    }

    private void PersistNewsSortMode(NewsEpisodeSortMode mode)
    {
        var ui = _settingsService.Current.Ui ??= new UiSettings();
        if (ui.NewsEpisodeSortMode == mode)
        {
            return;
        }

        ui.NewsEpisodeSortMode = mode;
        _settingsService.Save();
    }

    private void PersistTrackedShowViewMode(NewsTrackedShowViewMode mode)
    {
        var ui = _settingsService.Current.Ui ??= new UiSettings();
        if (ui.NewsTrackedShowViewMode == mode)
        {
            return;
        }

        ui.NewsTrackedShowViewMode = mode;
        _settingsService.Save();
    }

    private int CountPendingEpisodes(TrackedShow show, IReadOnlyList<TrackedEpisode> episodes)
    {
        if (!show.IsAutoTracked ||
            show.AutoTrackFromSeason is null ||
            show.AutoTrackFromEpisode is null)
        {
            return 0;
        }

        var today = AppTimeZone.Today;

        return episodes
            .Where(episode => IsAtOrAfterCheckpoint(
                episode,
                show.AutoTrackFromSeason.Value,
                show.AutoTrackFromEpisode.Value))
            .Where(episode => episode.AirDate is null || episode.AirDate.Value.Date <= today)
            .Count(episode =>
                episode.Availability == EpisodeAvailability.Missing &&
                string.IsNullOrWhiteSpace(episode.TorrentHash) &&
                !_torrentCartService.TryGetAutoTrackHuntBlockingEpisodeOrder(episode.Id, out _) &&
                !_torrentCartService.HasActiveManualEpisodeOrder(episode.Id));
    }

    private static bool IsShowNotYetAired(
        IReadOnlyList<TrackedEpisode> episodes,
        int fromSeason,
        int fromEpisode)
    {
        var today = AppTimeZone.Today;
        var tracked = episodes
            .Where(episode => episode.SeasonNumber != AppConstants.SpecialsSeasonNumber)
            .Where(episode => IsAtOrAfterCheckpoint(episode, fromSeason, fromEpisode))
            .ToList();

        if (tracked.Count == 0)
        {
            return true;
        }

        return tracked.All(episode =>
            episode.AirDate is null || episode.AirDate.Value.Date > today);
    }

    private static bool IsAtOrAfterCheckpoint(TrackedEpisode episode, int fromSeason, int fromEpisode)
    {
        return episode.SeasonNumber > fromSeason ||
               (episode.SeasonNumber == fromSeason && episode.EpisodeNumber >= fromEpisode);
    }

    private async Task LoadShowPosterAsync(NewsShowCardViewModel card, TrackedShow show)
    {
        card.PosterImage = await _posterImageService.LoadAsync(
            show.PosterPath,
            MediaKind.TvEpisode,
            show.TmdbId,
            width: 342);
    }

    private async Task LoadEpisodeStillAsync(NewsEpisodeCardViewModel card, TrackedShow show)
    {
        ImageSource? image = null;
        if (!string.IsNullOrWhiteSpace(card.StillPath))
        {
            image = await _posterImageService.LoadStillAsync(
                card.StillPath,
                show.TmdbId,
                card.SeasonNumber,
                card.EpisodeNumber,
                width: 300);
        }

        if (image is null)
        {
            image = await _posterImageService.LoadAsync(
                show.PosterPath,
                MediaKind.TvEpisode,
                show.TmdbId,
                width: 342);
        }

        card.StillImage = image;
    }
}
