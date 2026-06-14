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

    public AutoTrackViewModel(
        ISettingsService settingsService,
        ITrackedShowService trackedShowService,
        ITorrentCartService torrentCartService,
        IAutoTrackService autoTrackService,
        IAutoTrackSchedulerService autoTrackSchedulerService,
        IDownloadFolderCatalogService downloadFolderCatalogService)
    {
        _settingsService = settingsService;
        _trackedShowService = trackedShowService;
        _torrentCartService = torrentCartService;
        _autoTrackService = autoTrackService;
        _autoTrackSchedulerService = autoTrackSchedulerService;
        _downloadFolderCatalogService = downloadFolderCatalogService;

        _autoTrackSchedulerService.RunCompleted += (_, result) =>
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
            ? $"Scheduled every {Math.Clamp(autoTrack.IntervalHours, 1, 168)} hour(s)"
            : "Scheduler disabled in settings";

        TrackedShows.Clear();
        NewEpisodesThisWeek.Clear();

        var shows = _trackedShowService.GetAutoTrackedShows();
        TrackedShowCount = shows.Count;
        var pendingTotal = 0;
        var weekStart = DateTime.UtcNow.Date.AddDays(-7);

        var folderOptions = _downloadFolderCatalogService.GetKnownDownloadFolders();

        foreach (var show in shows)
        {
            var pending = CountPendingEpisodes(show);
            pendingTotal += pending;
            TrackedShows.Add(new AutoTrackShowCardViewModel(
                show,
                pending,
                folderOptions,
                _trackedShowService));

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

    private int CountPendingEpisodes(TrackedShow show)
    {
        if (!show.IsAutoTracked)
        {
            return 0;
        }

        return _trackedShowService.GetEpisodes(show.Id)
            .Where(episode => IsAtOrAfterCheckpoint(
                episode,
                show.AutoTrackFromSeason!.Value,
                show.AutoTrackFromEpisode!.Value))
            .Count(episode =>
                episode.Availability == EpisodeAvailability.Missing &&
                string.IsNullOrWhiteSpace(episode.TorrentHash) &&
                !_torrentCartService.TryGetActiveEpisodeOrder(episode.Id, out _));
    }

    private static bool IsAtOrAfterCheckpoint(TrackedEpisode episode, int fromSeason, int fromEpisode)
    {
        return episode.SeasonNumber > fromSeason ||
               (episode.SeasonNumber == fromSeason && episode.EpisodeNumber >= fromEpisode);
    }

    private bool CanRunNow() => !IsRunning && !_autoTrackService.IsRunning;
}
