using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class AutoTrackViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITorrentCartService _torrentCartService;
    private readonly IAutoTrackService _autoTrackService;
    private readonly IAutoTrackSchedulerService _autoTrackSchedulerService;
    private readonly IDownloadFolderCatalogService _downloadFolderCatalogService;
    private readonly IPosterImageService _posterImageService;

    public AutoTrackViewModel(
        ISettingsService settingsService,
        ITrackedShowService trackedShowService,
        ITorrentCartService torrentCartService,
        IAutoTrackService autoTrackService,
        IAutoTrackSchedulerService autoTrackSchedulerService,
        IDownloadFolderCatalogService downloadFolderCatalogService,
        IPosterImageService posterImageService)
    {
        _settingsService = settingsService;
        _trackedShowService = trackedShowService;
        _torrentCartService = torrentCartService;
        _autoTrackService = autoTrackService;
        _autoTrackSchedulerService = autoTrackSchedulerService;
        _downloadFolderCatalogService = downloadFolderCatalogService;
        _posterImageService = posterImageService;

        _autoTrackSchedulerService.RunCompleted += (_, _) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(RefreshDashboard);
        };

        RefreshDashboard();
    }

    public ObservableCollection<AutoTrackShowCardViewModel> TrackedShows { get; } = [];

    public ObservableCollection<AutoTrackNewEpisodeViewModel> NewEpisodesThisWeek { get; } = [];

    [ObservableProperty]
    private string lastRunSummary = "No runs yet.";

    [ObservableProperty]
    private string schedulerStatus = string.Empty;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private int trackedShowCount;

    [ObservableProperty]
    private int totalPendingEpisodes;

    [ObservableProperty]
    private string tmdbRefreshesRemainingLabel = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [RelayCommand(CanExecute = nameof(CanRunNow))]
    private async Task RunNow()
    {
        IsRunning = true;
        RunNowCommand.NotifyCanExecuteChanged();
        try
        {
            StatusMessage = "Running auto-track...";
            var result = await _autoTrackService.RunAsync();
            StatusMessage = result.Summary;
            RefreshDashboard();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Auto-track failed: {ex.Message}";
        }
        finally
        {
            IsRunning = _autoTrackService.IsRunning;
            RunNowCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void RefreshDashboard()
    {
        var autoTrack = _settingsService.Current.AutoTrack ?? new AutoTrackSettings();
        LastRunSummary = string.IsNullOrWhiteSpace(autoTrack.LastRunSummary)
            ? "No runs yet."
            : autoTrack.LastRunSummary;
        SchedulerStatus = autoTrack.Enabled
            ? $"TMDB every {Math.Clamp(autoTrack.TmdbCheckIntervalMinutes, 5, 1440)}m (then hunt if schedule OK) · Resume hunt every {Math.Clamp(autoTrack.TorrentHuntIntervalMinutes, 15, 1440)}m · Reconcile every {Math.Clamp(autoTrack.ReconcileIntervalMinutes, 5, 1440)}m · Hunt schedule {autoTrack.AnchorDayOfWeek} {autoTrack.AnchorTimeLocal}"
            : "Scheduler disabled in settings";
        TmdbRefreshesRemainingLabel = BuildTmdbRefreshesRemainingLabel(autoTrack);

        TrackedShows.Clear();
        NewEpisodesThisWeek.Clear();

        var shows = _trackedShowService.GetAutoTrackedShows();
        TrackedShowCount = shows.Count;
        var pendingTotal = 0;
        var weekStart = DateTime.UtcNow.Date.AddDays(-7);

        var folderOptions = _downloadFolderCatalogService.GetKnownDownloadFolders();

        foreach (var show in shows)
        {
            var episodes = _trackedShowService.GetEpisodes(show.Id);
            var pending = CountPendingEpisodes(show, episodes);
            pendingTotal += pending;
            var latestPending = AutoTrackTmdbEligibility.FindLatestPendingEpisode(
                show,
                episodes,
                episodeId =>
                    _torrentCartService.TryGetAutoTrackHuntBlockingEpisodeOrder(episodeId, out _) ||
                    _torrentCartService.HasActiveManualEpisodeOrder(episodeId),
                huntDelayHours: 0);
            var card = new AutoTrackShowCardViewModel(
                show,
                pending,
                latestPending,
                autoTrack,
                folderOptions,
                _trackedShowService);
            TrackedShows.Add(card);
            _ = LoadShowPosterAsync(card, show);

            foreach (var episode in _trackedShowService.GetEpisodes(show.Id)
                         .Where(episode => episode.AirDate is not null && episode.AirDate.Value.Date >= weekStart)
                         .Where(episode => IsAtOrAfterCheckpoint(
                             episode,
                             show.AutoTrackFromSeason!.Value,
                             show.AutoTrackFromEpisode!.Value))
                         .OrderByDescending(episode => episode.AirDate))
            {
                NewEpisodesThisWeek.Add(new AutoTrackNewEpisodeViewModel(show, episode));
            }
        }

        TotalPendingEpisodes = pendingTotal;
        IsRunning = _autoTrackService.IsRunning;
        RunNowCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void StopTracking(AutoTrackShowCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        _trackedShowService.StopAutoTrack(card.ShowId);
        StatusMessage = $"Stopped auto-tracking {card.Title}.";
        RefreshDashboard();
    }

    private int CountPendingEpisodes(TrackedShow show, IReadOnlyList<TrackedEpisode> episodes)
    {
        if (!show.IsAutoTracked)
        {
            return 0;
        }

        var today = DateTime.Now.Date;

        return episodes
            .Where(episode => IsAtOrAfterCheckpoint(
                episode,
                show.AutoTrackFromSeason!.Value,
                show.AutoTrackFromEpisode!.Value))
            .Where(episode => episode.AirDate is null || episode.AirDate.Value.Date <= today)
            .Count(episode =>
                episode.Availability == EpisodeAvailability.Missing &&
                string.IsNullOrWhiteSpace(episode.TorrentHash) &&
                !_torrentCartService.TryGetAutoTrackHuntBlockingEpisodeOrder(episode.Id, out _) &&
                !_torrentCartService.HasActiveManualEpisodeOrder(episode.Id));
    }

    private static bool IsAtOrAfterCheckpoint(TrackedEpisode episode, int fromSeason, int fromEpisode)
    {
        return episode.SeasonNumber > fromSeason ||
               (episode.SeasonNumber == fromSeason && episode.EpisodeNumber >= fromEpisode);
    }

    private static string BuildTmdbRefreshesRemainingLabel(AutoTrackSettings autoTrack)
    {
        var max = Math.Clamp(autoTrack.MaxTmdbRefreshesPerDay, 1, 500);
        var nowLocal = DateTime.Now;
        var budget = autoTrack.DailyBudget ?? new TmdbDailyBudget();
        var remaining = budget.Remaining(max, nowLocal);
        var used = budget.IsExpiredFor(nowLocal) ? 0 : Math.Max(0, budget.Used);
        return $"TMDB refreshes left today: {remaining} ({used}/{max} used)";
    }

    private bool CanRunNow() => !IsRunning && !_autoTrackService.IsRunning;

    private async Task LoadShowPosterAsync(AutoTrackShowCardViewModel card, TrackedShow show)
    {
        card.PosterImage = await _posterImageService.LoadAsync(
            show.PosterPath,
            MediaKind.TvEpisode,
            show.TmdbId,
            width: 154);
    }
}
