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
    private readonly IRecipeService _recipeService;

    private readonly HashSet<long> _expandedRecipeShowIds = [];

    public AutoTrackViewModel(
        ISettingsService settingsService,
        ITrackedShowService trackedShowService,
        ITorrentCartService torrentCartService,
        IAutoTrackService autoTrackService,
        IAutoTrackSchedulerService autoTrackSchedulerService,
        IDownloadFolderCatalogService downloadFolderCatalogService,
        IPosterImageService posterImageService,
        IRecipeService recipeService)
    {
        _settingsService = settingsService;
        _trackedShowService = trackedShowService;
        _torrentCartService = torrentCartService;
        _autoTrackService = autoTrackService;
        _autoTrackSchedulerService = autoTrackSchedulerService;
        _downloadFolderCatalogService = downloadFolderCatalogService;
        _posterImageService = posterImageService;
        _recipeService = recipeService;

        _autoTrackSchedulerService.RunCompleted += (_, _) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(RefreshDashboard);
        };

        RefreshDashboard();
    }

    public ObservableCollection<AutoTrackShowCardViewModel> TrackedShows { get; } = [];

    public ObservableCollection<AutoTrackShowCardViewModel> FilteredTrackedShows { get; } = [];

    public ObservableCollection<AutoTrackNewEpisodeViewModel> NewEpisodesThisWeek { get; } = [];

    public string RecipeHelpTooltip { get; } =
        "Episode recipe is assigned in Cart Order. Auto-Track cannot change it. Overrides here apply only to Auto-Track hunts, not Run Cart.";

    [ObservableProperty]
    private string lastRunSummary = "No runs yet.";

    [ObservableProperty]
    private string schedulerStatus = string.Empty;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string statusLabel = "Idle";

    [ObservableProperty]
    private int trackedShowCount;

    [ObservableProperty]
    private int totalPendingEpisodes;

    [ObservableProperty]
    private string tmdbRefreshesRemainingLabel = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string showSearchText = string.Empty;

    [ObservableProperty]
    private string showSearchQuery = string.Empty;

    public bool HasShowSearchText => !string.IsNullOrEmpty(ShowSearchText);

    public bool HasAppliedShowSearch => !string.IsNullOrWhiteSpace(ShowSearchQuery);

    public bool HasNoShowSearchResults => HasAppliedShowSearch && FilteredTrackedShows.Count == 0;

    public string ShowsPendingLabel => $"{TrackedShowCount} · {TotalPendingEpisodes} pending";

    [RelayCommand(CanExecute = nameof(CanRunNow))]
    private async Task RunNow()
    {
        IsRunning = true;
        RunNowCommand.NotifyCanExecuteChanged();
        try
        {
            StatusMessage = "Running auto-track...";
            var result = await _autoTrackService.RunAsync();
            if (result.TorrentsAdded > 0)
            {
                _autoTrackSchedulerService.RequestReconcileAfterAdds();
            }

            StatusMessage = result.FormatHumanSummary();
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

    public override void OnNavigatedTo()
    {
        RefreshDashboard();
    }

    [RelayCommand]
    private void RefreshDashboard()
    {
        var autoTrack = _settingsService.Current.AutoTrack ?? new AutoTrackSettings();
        LastRunSummary = string.IsNullOrWhiteSpace(autoTrack.LastRunSummary)
            ? "No runs yet."
            : autoTrack.LastRunSummary;
        SchedulerStatus = autoTrack.Enabled
            ? $"TMDB every {Math.Clamp(autoTrack.TmdbCheckIntervalMinutes, 5, 1440)}m (hunt only when pending episodes) · Reconcile poll while downloads pending every {Math.Clamp(autoTrack.ReconcileIntervalMinutes, 5, 1440)}m · {(autoTrack.EnforceGlobalWeeklySchedule ? $"Hunt schedule {autoTrack.AnchorDayOfWeek} {autoTrack.AnchorTimeLocal}" : "Default weekly schedule off")}"
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
            var maxEpisodesPerShow = Math.Clamp(autoTrack.Search?.MaxEpisodesPerShowPerHuntCycle ?? 5, 1, 50);
            var huntBatch = AutoTrackTmdbEligibility.FindPendingEpisodes(
                    show,
                    episodes,
                    episodeId =>
                        _torrentCartService.TryGetAutoTrackHuntBlockingEpisodeOrder(episodeId, out _) ||
                        _torrentCartService.HasActiveManualEpisodeOrder(episodeId),
                    huntDelayHours: 0)
                .Take(maxEpisodesPerShow)
                .ToList();
            var card = new AutoTrackShowCardViewModel(
                show,
                pending,
                huntBatch,
                autoTrack,
                folderOptions,
                _trackedShowService,
                _recipeService,
                _settingsService.Current.AutoTorrent,
                GetLastHuntFailureDetail(show.Id),
                overviewExpanded: _expandedRecipeShowIds.Contains(show.Id),
                onOverviewExpandedChanged: expanded => UpdateExpandedRecipe(show.Id, expanded));
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
        ApplyShowFilter();
    }

    [RelayCommand]
    private void SearchShows()
    {
        ShowSearchText = (ShowSearchText ?? string.Empty).Trim();
        ShowSearchQuery = ShowSearchText;
    }

    [RelayCommand(CanExecute = nameof(CanClearShowSearch))]
    private void ClearShowSearch()
    {
        ShowSearchText = string.Empty;
        ShowSearchQuery = string.Empty;
    }

    private bool CanClearShowSearch() => HasAppliedShowSearch;

    [RelayCommand]
    private void ExpandAllRecipes()
    {
        foreach (var card in TrackedShows)
        {
            card.EpisodeRecipeSummary.IsOverviewExpanded = true;
            _expandedRecipeShowIds.Add(card.ShowId);
        }
    }

    [RelayCommand]
    private void CollapseAllRecipes()
    {
        _expandedRecipeShowIds.Clear();
        foreach (var card in TrackedShows)
        {
            card.EpisodeRecipeSummary.IsOverviewExpanded = false;
        }
    }

    partial void OnShowSearchTextChanged(string value) =>
        OnPropertyChanged(nameof(HasShowSearchText));

    partial void OnShowSearchQueryChanged(string value)
    {
        ApplyShowFilter();
        OnPropertyChanged(nameof(HasAppliedShowSearch));
        OnPropertyChanged(nameof(HasNoShowSearchResults));
        ClearShowSearchCommand.NotifyCanExecuteChanged();
    }

    private void ApplyShowFilter()
    {
        FilteredTrackedShows.Clear();
        var query = ShowSearchQuery.Trim();
        var source = string.IsNullOrWhiteSpace(query)
            ? TrackedShows.AsEnumerable()
            : TrackedShows.Where(card =>
                card.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
        foreach (var card in source)
        {
            FilteredTrackedShows.Add(card);
        }

        OnPropertyChanged(nameof(HasNoShowSearchResults));
    }

    private void UpdateExpandedRecipe(long showId, bool expanded)
    {
        if (expanded)
        {
            _expandedRecipeShowIds.Add(showId);
            return;
        }

        _expandedRecipeShowIds.Remove(showId);
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

    [RelayCommand]
    private void ResetWeek(AutoTrackShowCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        _trackedShowService.ResetAutoTrackWeekSatisfaction(card.ShowId);
        var released = _torrentCartService.ReleaseAutoTrackHuntBlocks(card.ShowId);
        StatusMessage = released > 0
            ? $"Reset week for {card.Title} (cleared {released} blocking cart order(s))."
            : $"Reset week satisfaction for {card.Title}.";
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
        return $"{remaining} left today ({used}/{max} used)";
    }

    private string? GetLastHuntFailureDetail(long showId)
    {
        var order = _torrentCartService.GetOrders(MediaKind.TvEpisode, showId)
            .Where(item => item.Source == TorrentOrderSource.AutoTrack)
            .Where(item => item.Status is TorrentOrderStatus.NoCandidates or TorrentOrderStatus.Failed)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(order?.StatusDetail) ? null : order.StatusDetail.Trim();
    }

    private bool CanRunNow() => !IsRunning && !_autoTrackService.IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        StatusLabel = value ? "Running" : "Idle";
    }

    partial void OnTrackedShowCountChanged(int value) => OnPropertyChanged(nameof(ShowsPendingLabel));

    partial void OnTotalPendingEpisodesChanged(int value) => OnPropertyChanged(nameof(ShowsPendingLabel));

    private async Task LoadShowPosterAsync(AutoTrackShowCardViewModel card, TrackedShow show)
    {
        card.PosterImage = await _posterImageService.LoadAsync(
            show.PosterPath,
            MediaKind.TvEpisode,
            show.TmdbId,
            width: 154);
    }
}
