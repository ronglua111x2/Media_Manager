using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public partial class AutoTorrentViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly IFetchJobService _fetchJobService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IAutoTorrentLinkService _autoTorrentLinkService;
    private readonly IDatabaseService _databaseService;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _storageStatusTimer;
    private CancellationTokenSource? _packFetchCancellation;
    private bool _isQbittorrentRefreshRunning;

    [ObservableProperty]
    private string showSearchText = string.Empty;

    [ObservableProperty]
    private TmdbShowSearchResult? selectedShowSearchResult;

    [ObservableProperty]
    private string movieSearchText = string.Empty;

    [ObservableProperty]
    private TmdbMovieSearchResult? selectedMovieSearchResult;

    [ObservableProperty]
    private FetchJobRowViewModel? selectedFetchJob;

    [ObservableProperty]
    private string statusMessage = "Search TMDb to add a tracked show.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isPackFetchRunning;

    public AutoTorrentViewModel(
        ISettingsService settingsService,
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        IFetchJobService fetchJobService,
        IQbittorrentClient qbittorrentClient,
        IAutoTorrentLinkService autoTorrentLinkService,
        IDatabaseService databaseService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _fetchJobService = fetchJobService;
        _qbittorrentClient = qbittorrentClient;
        _autoTorrentLinkService = autoTorrentLinkService;
        _databaseService = databaseService;
        _logger = logger;

        ShowSearchResults = [];
        MovieSearchResults = [];
        ShowCards = [];
        MovieCards = [];
        FetchJobs = [];
        StorageStatuses = [];
        _fetchJobService.JobsChanged += OnFetchJobsChanged;
        _fetchJobService.CandidatesChanged += OnCandidatesChanged;
        ReloadShowCards();
        ReloadMovieCards();
        ReloadFetchJobs();
        RefreshStorageStatus(updateStatusMessage: false);
        _storageStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _storageStatusTimer.Tick += (_, _) => RefreshStorageStatus(updateStatusMessage: false);
        _storageStatusTimer.Start();
    }

    public ObservableCollection<TmdbShowSearchResult> ShowSearchResults { get; }

    public ObservableCollection<TmdbMovieSearchResult> MovieSearchResults { get; }

    public ObservableCollection<TrackedShowCardViewModel> ShowCards { get; }

    public ObservableCollection<TrackedMovieCardViewModel> MovieCards { get; }

    public ObservableCollection<FetchJobRowViewModel> FetchJobs { get; }

    public ObservableCollection<StorageStatusViewModel> StorageStatuses { get; }

    [RelayCommand]
    private async Task SearchShows()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ShowSearchText))
        {
            StatusMessage = "Enter a show title to search TMDb.";
            return;
        }

        await RunAsync(async () =>
        {
            ShowSearchResults.Clear();
            SelectedShowSearchResult = null;
            StatusMessage = $"Searching TMDb: {ShowSearchText}";
            var results = await _trackedShowService.SearchShowsAsync(ShowSearchText);
            foreach (var result in results)
            {
                ShowSearchResults.Add(result);
            }

            StatusMessage = results.Count == 0
                ? "TMDb returned no show results."
                : $"TMDb returned {results.Count} show result(s).";
        });
    }

    [RelayCommand]
    private async Task AddSelectedShow()
    {
        if (IsBusy)
        {
            return;
        }

        if (SelectedShowSearchResult is null)
        {
            StatusMessage = "Select a TMDb show result first.";
            return;
        }

        await RunAsync(async () =>
        {
            StatusMessage = $"Importing show: {SelectedShowSearchResult.DisplayTitle}";
            var show = await _trackedShowService.AddShowAsync(SelectedShowSearchResult);
            ReloadShowCards(show.Id);
            StatusMessage = $"Imported {show.DisplayTitle}. Aired episodes: {show.TotalEpisodes}.";
        });
    }

    [RelayCommand]
    private async Task SearchMovies()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(MovieSearchText))
        {
            StatusMessage = "Enter a movie title to search TMDb.";
            return;
        }

        await RunAsync(async () =>
        {
            MovieSearchResults.Clear();
            SelectedMovieSearchResult = null;
            StatusMessage = $"Searching TMDb movies: {MovieSearchText}";
            var results = await _trackedMovieService.SearchMoviesAsync(MovieSearchText);
            foreach (var result in results)
            {
                MovieSearchResults.Add(result);
            }

            StatusMessage = results.Count == 0
                ? "TMDb returned no movie results."
                : $"TMDb returned {results.Count} movie result(s).";
        });
    }

    [RelayCommand]
    private async Task AddSelectedMovie()
    {
        if (IsBusy)
        {
            return;
        }

        if (SelectedMovieSearchResult is null)
        {
            StatusMessage = "Select a TMDb movie result first.";
            return;
        }

        await RunAsync(async () =>
        {
            StatusMessage = $"Importing movie: {SelectedMovieSearchResult.DisplayTitle}";
            var movie = await _trackedMovieService.AddMovieAsync(SelectedMovieSearchResult);
            ReloadMovieCards(movie.Id);
            StatusMessage = $"Imported movie {movie.DisplayTitle}.";
        });
    }

    [RelayCommand]
    private async Task RefreshShow(TrackedShowCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            StatusMessage = $"Refreshing {card.Title}...";
            var show = await _trackedShowService.RefreshShowAsync(card.Show);
            ReloadShowCards(show.Id);
            StatusMessage = $"Refreshed {show.DisplayTitle}.";
        });
    }

    [RelayCommand]
    private async Task RefreshMovieTmdb(TrackedMovieCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            StatusMessage = $"Refreshing movie metadata: {card.Title}...";
            var movie = await _trackedMovieService.RefreshMovieAsync(card.Movie);
            ReloadMovieCards(movie.Id);
            StatusMessage = $"Refreshed movie {movie.DisplayTitle}.";
        });
    }

    [RelayCommand]
    private async Task FetchWanted(TrackedShowCardViewModel? card)
    {
        if (card is null)
        {
            StatusMessage = "Select a show to fetch.";
            return;
        }

        await RunAsync(async () =>
        {
            var job = await _fetchJobService.EnqueueShowFetchAsync(card.Id);
            StatusMessage = $"Queued fetch job #{job.Id} for {card.Title}.";
            ReloadFetchJobs();
        });
    }

    [RelayCommand]
    private async Task FetchMovieWanted(TrackedMovieCardViewModel? card)
    {
        if (card is null)
        {
            StatusMessage = "Select a movie to fetch.";
            return;
        }

        await RunAsync(async () =>
        {
            var job = await _fetchJobService.EnqueueMovieFetchAsync(card.Id);
            StatusMessage = $"Queued movie fetch job #{job.Id} for {card.Title}.";
            ReloadFetchJobs();
        });
    }

    [RelayCommand]
    private async Task AddApproved(TrackedShowCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        var approvedEpisodes = card.Seasons
            .Where(season => !season.IsPackMode)
            .SelectMany(season => season.Episodes)
            .Where(episode => episode.SelectedCandidate is not null)
            .ToList();
        if (approvedEpisodes.Count == 0)
        {
            StatusMessage = "No selected candidates to add.";
            return;
        }

        await RunAsync(async () =>
        {
            await AddEpisodesToQbittorrentCore(approvedEpisodes);
        });
    }

    [RelayCommand]
    private async Task AddWantedSelected(TrackedShowCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        var wantedEpisodes = card.Seasons
            .Where(season => !season.IsPackMode)
            .SelectMany(season => season.Episodes)
            .Where(episode => episode.IsWanted && episode.SelectedCandidate is not null)
            .ToList();
        if (wantedEpisodes.Count == 0)
        {
            StatusMessage = "No wanted episodes have selected candidates.";
            return;
        }

        await AddEpisodesToQbittorrent(wantedEpisodes);
    }

    [RelayCommand]
    private async Task FetchPacksForSelected(TrackedShowCardViewModel? card)
    {
        if (IsBusy || IsPackFetchRunning || card is null)
        {
            return;
        }

        var seasons = card.Seasons
            .Where(season => season.IsPackMode)
            .Select(season => season.SeasonNumber)
            .ToList();
        if (seasons.Count == 0)
        {
            StatusMessage = "Select at least one season in Pack mode.";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _packFetchCancellation = cancellation;
        IsPackFetchRunning = true;
        await RunAsync(async () =>
        {
            try
            {
                StatusMessage = $"Fetching pack candidates for {card.Title}.";
                await _fetchJobService.FetchSeasonPacksAsync(card.Id, seasons, cancellation.Token);
                RefreshCandidatesInPlace();
                StatusMessage = $"Fetched pack candidates for {card.Title}.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = $"Canceled pack fetch for {card.Title}.";
                _logger.Warning(StatusMessage, Common.LogTarget.All);
            }
        });
        if (ReferenceEquals(_packFetchCancellation, cancellation))
        {
            _packFetchCancellation = null;
        }

        IsPackFetchRunning = false;
    }

    [RelayCommand]
    private void AbortFetchPacks()
    {
        if (!IsPackFetchRunning || _packFetchCancellation is null)
        {
            StatusMessage = "No season pack fetch is running.";
            return;
        }

        _packFetchCancellation.Cancel();
        StatusMessage = "Abort requested for season pack fetch.";
    }

    [RelayCommand]
    private void SaveSeasonPack(TrackedSeasonViewModel? season)
    {
        if (season?.SelectedPackCandidate is null)
        {
            StatusMessage = "Select a pack candidate first.";
            return;
        }
        if (!season.IsPackMode)
        {
            StatusMessage = $"Enable Pack mode for S{season.SeasonNumber:00} before saving a pack.";
            return;
        }

        var candidate = season.SelectedPackCandidate;
        _trackedShowService.ClearSeasonSelectedPacksForSeasons(
            candidate.ShowId,
            candidate.CoveredSeasons.Count == 0 ? [season.SeasonNumber] : candidate.CoveredSeasons);
        _trackedShowService.UpdateSeasonSelectedPack(candidate.ShowId, season.SeasonNumber, candidate);
        ReloadShowCards(candidate.ShowId, season.SeasonNumber);
        StatusMessage = $"Saved pack for S{season.SeasonNumber:00}.";
    }

    [RelayCommand]
    private void DeselectSeasonPack(TrackedSeasonViewModel? season)
    {
        if (season is null)
        {
            return;
        }

        _trackedShowService.ClearSeasonSelectedPack(season.ShowId, season.SelectedPackOwnerSeasonNumber ?? season.SeasonNumber);
        ReloadShowCards(season.ShowId, season.SelectedPackOwnerSeasonNumber ?? season.SeasonNumber);
        StatusMessage = $"Cleared pack for S{season.SeasonNumber:00}.";
    }

    [RelayCommand]
    private async Task AddSeasonPack(TrackedSeasonViewModel? season)
    {
        if (IsBusy || season is null)
        {
            StatusMessage = "Select a pack row first.";
            return;
        }

        var ownerSeason = FindPackOwnerSeason(season);
        if (ownerSeason is null)
        {
            StatusMessage = $"Select owner season S{season.SelectedPackOwnerSeasonNumber:00} to add this pack.";
            return;
        }

        if (ownerSeason.SelectedPackCandidate is null)
        {
            StatusMessage = "Select a pack candidate first.";
            return;
        }
        if (!string.IsNullOrWhiteSpace(ownerSeason.PackTorrentStatus))
        {
            StatusMessage = $"Pack for S{ownerSeason.SeasonNumber:00} is already mapped: {ownerSeason.PackTorrentStatus}.";
            return;
        }

        var candidate = ownerSeason.SelectedPackCandidate;
        await RunAsync(async () =>
        {
            var savePath = GetSeasonDownloadFolder(ownerSeason);
            var addedTorrent = await _qbittorrentClient.AddTorrentAsync(new AddTorrentRequest
            {
                Url = candidate.FileUrl,
                PluginName = candidate.PluginName,
                SavePath = savePath,
                Category = string.IsNullOrWhiteSpace(_settingsService.Current.AutoTorrent.CategoryName)
                    ? "AutoTorrent"
                    : _settingsService.Current.AutoTorrent.CategoryName,
                Paused = false
            });
            _trackedShowService.UpdateSeasonSelectedPack(candidate.ShowId, ownerSeason.SeasonNumber, candidate);
            _trackedShowService.UpdateSeasonPackTorrent(candidate.ShowId, ownerSeason.SeasonNumber, addedTorrent);
            ReloadShowCards(candidate.ShowId, ownerSeason.SeasonNumber);
            StatusMessage = $"Added pack torrent for S{ownerSeason.SeasonNumber:00} covering {candidate.CoveredSeasonsDisplay}: {addedTorrent.Name}. Save path: {savePath}";
        });
    }

    [RelayCommand]
    private async Task LinkSeasonPackCompleted(TrackedSeasonViewModel? season)
    {
        if (IsBusy || season is null)
        {
            StatusMessage = "Select a pack row first.";
            return;
        }

        var ownerSeasonNumber = season.SelectedPackOwnerSeasonNumber ?? season.SeasonNumber;
        await RunAsync(async () =>
        {
            StatusMessage = $"Creating library links for pack owner S{ownerSeasonNumber:00}...";
            var result = await _autoTorrentLinkService.LinkSeasonPackAsync(season.ShowId, ownerSeasonNumber);
            _trackedShowService.RefreshAvailability(season.ShowId);
            ReloadShowCards(season.ShowId, ownerSeasonNumber);
            StatusMessage = $"Pack library links for S{ownerSeasonNumber:00}: {result.Summary}.";
            LogLinkMessages(result);
        });
    }

    [RelayCommand]
    private void SaveSelectedCandidates(TrackedShowCardViewModel? card)
    {
        if (card is null)
        {
            StatusMessage = "Select a show first.";
            return;
        }

        var savedCount = 0;
        foreach (var episode in card.Seasons.Where(season => !season.IsPackMode).SelectMany(season => season.Episodes))
        {
            var candidate = episode.SelectedCandidate?.Candidate;
            if (candidate is null)
            {
                continue;
            }

            _trackedShowService.UpdateSelectedCandidate(episode.Id, candidate);
            savedCount++;
        }

        StatusMessage = $"Saved {savedCount} selected candidate(s) for {card.Title}.";
    }

    [RelayCommand]
    private async Task AddMovieApproved(TrackedMovieCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        if (!card.IsWanted)
        {
            StatusMessage = "Movie is not marked wanted.";
            return;
        }

        var candidate = card.SelectedCandidate?.Candidate;
        if (candidate is null)
        {
            StatusMessage = "Movie has no selected candidate.";
            return;
        }

        await RunAsync(async () =>
        {
            _logger.Info(
                $"Adding torrent for movie {card.Title}: Candidate='{candidate.FileName}', Seeders={candidate.Seeders}, Plugin='{candidate.PluginName}'",
                Common.LogTarget.All);
            var addedTorrent = await _qbittorrentClient.AddTorrentAsync(new AddTorrentRequest
            {
                Url = candidate.FileUrl,
                PluginName = candidate.PluginName,
                SavePath = GetDefaultDownloadFolder(),
                Category = string.IsNullOrWhiteSpace(_settingsService.Current.AutoTorrent.CategoryName)
                    ? "AutoTorrent"
                    : _settingsService.Current.AutoTorrent.CategoryName,
                Paused = false
            });
            _trackedMovieService.UpdateTorrentState(card.Id, addedTorrent);
            card.MarkTorrentAdded(addedTorrent);
            StatusMessage = $"Added movie torrent to qBittorrent: {addedTorrent.Name}";
        });
    }

    [RelayCommand]
    private void SaveMovieSelectedCandidate(TrackedMovieCardViewModel? card)
    {
        if (card?.SelectedCandidate?.Candidate is null)
        {
            StatusMessage = "Movie has no selected candidate to save.";
            return;
        }

        _trackedMovieService.UpdateSelectedCandidate(card.Id, card.SelectedCandidate.Candidate);
        StatusMessage = $"Saved selected candidate for {card.Title}.";
    }

    [RelayCommand]
    private async Task RefreshTorrentStates()
    {
        await RunAsync(async () =>
        {
            var result = await RefreshShowQbittorrentStateCoreAsync(showId: null, logDetails: false);
            ReloadShowCards();
            StatusMessage = result.Skipped
                ? "qBittorrent refresh is already running."
                : $"Refreshed availability and qBittorrent state: {result.UpdatedCount} tracked, {result.RemovedCount} removed.";
        });
    }

    [RelayCommand]
    private async Task LinkShowCompleted(TrackedShowCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            StatusMessage = $"Creating library links for {card.Title}...";
            var result = await _autoTorrentLinkService.LinkShowAsync(card.Id);
            _trackedShowService.RefreshAvailability(card.Id);
            ReloadShowCards(card.Id);
            StatusMessage = $"Library links for {card.Title}: {result.Summary}.";
            LogLinkMessages(result);
        });
    }

    [RelayCommand]
    private void RemoveShowLinks(TrackedShowCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        var result = _autoTorrentLinkService.RemoveShowLinks(card.Id);
        _trackedShowService.RefreshAvailability(card.Id);
        ReloadShowCards(card.Id);
        StatusMessage = $"Removed library links for {card.Title}: {result.Summary}.";
        LogLinkMessages(result);
    }

    [RelayCommand]
    private void RefreshShowLinks(TrackedShowCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        var result = _autoTorrentLinkService.RefreshLinkStatus(showId: card.Id);
        _trackedShowService.RefreshAvailability(card.Id);
        ReloadShowCards(card.Id);
        StatusMessage = $"Refreshed library links for {card.Title}: {result.Summary}.";
        LogLinkMessages(result);
    }

    [RelayCommand]
    private async Task RefreshShowQbittorrent(TrackedShowCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            StatusMessage = $"Updating qBittorrent state for {card.Title}...";
            var result = await RefreshShowQbittorrentStateCoreAsync(card.Id, logDetails: true);
            ReloadShowCards(card.Id);
            StatusMessage = result.Skipped
                ? "qBittorrent refresh is already running."
                : $"Updated qBittorrent for {card.Title}: {result.UpdatedCount} tracked, {result.RemovedCount} removed.";
        });
    }

    [RelayCommand]
    private async Task RefreshMovieQbittorrent(TrackedMovieCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            StatusMessage = $"Updating qBittorrent state for {card.Title}...";
            _trackedMovieService.RefreshAvailability(card.Id);
            if (string.IsNullOrWhiteSpace(card.TorrentHash))
            {
                ReloadMovieCards(card.Id);
                StatusMessage = $"No mapped torrent for {card.Title}.";
                return;
            }

            var torrent = (await _qbittorrentClient.GetTorrentsAsync())
                .FirstOrDefault(item => string.Equals(item.Hash, card.TorrentHash, StringComparison.OrdinalIgnoreCase));
            if (torrent is null)
            {
                _trackedMovieService.MarkTorrentRemoved(card.Id, card.TorrentHash);
                card.MarkTorrentRemoved();
                ReloadMovieCards(card.Id);
                StatusMessage = $"Mapped torrent for {card.Title} was removed from qBittorrent.";
                return;
            }

            _trackedMovieService.UpdateTorrentState(card.Id, torrent);
            card.UpdateTorrentStatus(torrent);
            ReloadMovieCards(card.Id);
            StatusMessage = $"Updated qBittorrent for {card.Title}: {torrent.State} {torrent.ProgressDisplay}.";
        });
    }

    [RelayCommand]
    private async Task LinkMovieCompleted(TrackedMovieCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            StatusMessage = $"Creating library link for {card.Title}...";
            var result = await _autoTorrentLinkService.LinkMovieAsync(card.Id);
            _trackedMovieService.RefreshAvailability(card.Id);
            ReloadMovieCards(card.Id);
            StatusMessage = $"Library links for {card.Title}: {result.Summary}.";
            LogLinkMessages(result);
        });
    }

    [RelayCommand]
    private void RemoveMovieLinks(TrackedMovieCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        var result = _autoTorrentLinkService.RemoveMovieLinks(card.Id);
        _trackedMovieService.RefreshAvailability(card.Id);
        ReloadMovieCards(card.Id);
        StatusMessage = $"Removed library links for {card.Title}: {result.Summary}.";
        LogLinkMessages(result);
    }

    [RelayCommand]
    private void RefreshMovieLinks(TrackedMovieCardViewModel? card)
    {
        if (IsBusy || card is null)
        {
            return;
        }

        var result = _autoTorrentLinkService.RefreshLinkStatus(movieId: card.Id);
        _trackedMovieService.RefreshAvailability(card.Id);
        ReloadMovieCards(card.Id);
        StatusMessage = $"Refreshed library links for {card.Title}: {result.Summary}.";
        LogLinkMessages(result);
    }

    private async Task AddEpisodesToQbittorrent(IReadOnlyList<TrackedEpisodeRowViewModel> episodes)
    {
        await RunAsync(async () => await AddEpisodesToQbittorrentCore(episodes));
    }

    private async Task AddEpisodesToQbittorrentCore(IReadOnlyList<TrackedEpisodeRowViewModel> episodes)
    {
        var addedCount = 0;
        foreach (var episode in episodes)
        {
            var candidate = episode.SelectedCandidate?.Candidate;
            if (candidate is null)
            {
                continue;
            }

            _logger.Info(
                $"Adding torrent for episode {episode.EpisodeCode}: Candidate='{candidate.FileName}', Seeders={candidate.Seeders}, Plugin='{candidate.PluginName}'",
                Common.LogTarget.All);
            var addedTorrent = await _qbittorrentClient.AddTorrentAsync(new AddTorrentRequest
            {
                Url = candidate.FileUrl,
                PluginName = candidate.PluginName,
                SavePath = GetEpisodeDownloadFolder(episode),
                Category = string.IsNullOrWhiteSpace(_settingsService.Current.AutoTorrent.CategoryName)
                    ? "AutoTorrent"
                    : _settingsService.Current.AutoTorrent.CategoryName,
                Paused = false
            });
            _trackedShowService.UpdateTorrentState(episode.Id, addedTorrent);
            episode.MarkTorrentAdded(addedTorrent);
            addedCount++;
            _logger.Info(
                $"Torrent add verified for {episode.EpisodeCode}: Name='{addedTorrent.Name}', Hash={addedTorrent.Hash}, State={addedTorrent.State}, Progress={addedTorrent.ProgressDisplay}",
                Common.LogTarget.All);
        }

        StatusMessage = $"Added {addedCount} torrent(s) to qBittorrent.";
    }

    [RelayCommand]
    private void RefreshAvailability()
    {
        _trackedShowService.RefreshAvailability();
        _trackedMovieService.RefreshAvailability();
        ReloadShowCards();
        ReloadMovieCards();
        StatusMessage = "Refreshed tracked TV and movie availability from local SourceItems.";
    }

    private void RefreshStorageStatus(bool updateStatusMessage)
    {
        StorageStatuses.Clear();
        foreach (var folder in GetKnownDownloadFolders().Where(Directory.Exists))
        {
            var root = Path.GetPathRoot(folder);
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            try
            {
                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    continue;
                }

                if (StorageStatuses.Any(status => string.Equals(status.DriveRoot, drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                StorageStatuses.Add(new StorageStatusViewModel
                {
                    DriveRoot = drive.RootDirectory.FullName,
                    Folder = folder,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace
                });
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not read storage status for folder '{folder}'. {ex.Message}", Common.LogTarget.All);
            }
        }

        if (updateStatusMessage)
        {
            StatusMessage = $"Updated storage status for {StorageStatuses.Count} drive(s).";
        }
    }

    [RelayCommand]
    private void CancelSelectedJob()
    {
        if (SelectedFetchJob is null)
        {
            StatusMessage = "Select a fetch job first.";
            return;
        }

        _fetchJobService.CancelJob(SelectedFetchJob.Id);
        StatusMessage = $"Cancel requested for fetch job #{SelectedFetchJob.Id}.";
    }

    [RelayCommand]
    private async Task RetrySelectedJob()
    {
        if (SelectedFetchJob is null)
        {
            StatusMessage = "Select a fetch job first.";
            return;
        }

        var job = await _fetchJobService.RetryJobAsync(SelectedFetchJob.Id);
        StatusMessage = $"Queued retry job #{job.Id}.";
        ReloadFetchJobs();
    }

    [RelayCommand]
    private void DeleteSelectedJob()
    {
        if (SelectedFetchJob is null)
        {
            StatusMessage = "Select a fetch job first.";
            return;
        }

        var jobId = SelectedFetchJob.Id;
        _fetchJobService.DeleteJob(jobId);
        StatusMessage = $"Deleted fetch job #{jobId}.";
        ReloadFetchJobs();
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = "Network request failed. Check TMDb/qBittorrent settings.";
            _logger.Error(StatusMessage, ex, Common.LogTarget.All);
        }
        catch (TaskCanceledException ex)
        {
            StatusMessage = "Operation timed out or was canceled.";
            _logger.Error(StatusMessage, ex, Common.LogTarget.All);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.Error($"Auto Torrent operation failed: {ex.Message}", ex, Common.LogTarget.All);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LogLinkMessages(AutoTorrentLinkResult result)
    {
        foreach (var message in result.Messages.Take(20))
        {
            _logger.Info(message, Common.LogTarget.All);
        }

        if (result.Messages.Count > 20)
        {
            _logger.Info($"Skipped logging {result.Messages.Count - 20} additional link message(s).", Common.LogTarget.All);
        }
    }

    private async Task<ShowQbittorrentRefreshResult> RefreshShowQbittorrentStateCoreAsync(long? showId, bool logDetails)
    {
        if (_isQbittorrentRefreshRunning)
        {
            return new ShowQbittorrentRefreshResult(Skipped: true, EpisodeUpdatedCount: 0, PackUpdatedCount: 0, EpisodeRemovedCount: 0, PackRemovedCount: 0);
        }

        _isQbittorrentRefreshRunning = true;
        try
        {
            if (showId is null)
            {
                _trackedShowService.RefreshAvailability();
            }
            else
            {
                _trackedShowService.RefreshAvailability(showId.Value);
            }

            var torrentsByHash = (await _qbittorrentClient.GetTorrentsAsync())
                .ToDictionary(torrent => torrent.Hash, StringComparer.OrdinalIgnoreCase);
            var showsById = _trackedShowService.GetShows().ToDictionary(show => show.Id);
            var showIds = showId is null
                ? showsById.Keys.ToList()
                : [showId.Value];
            var episodeUpdatedCount = 0;
            var packUpdatedCount = 0;
            var episodeRemovedCount = 0;
            var packRemovedCount = 0;

            foreach (var trackedShowId in showIds)
            {
                showsById.TryGetValue(trackedShowId, out var show);
                foreach (var episode in _trackedShowService.GetEpisodes(trackedShowId)
                             .Where(episode => !string.IsNullOrWhiteSpace(episode.TorrentHash)))
                {
                    if (torrentsByHash.TryGetValue(episode.TorrentHash!, out var torrent))
                    {
                        _trackedShowService.UpdateTorrentState(episode.Id, torrent);
                        episodeUpdatedCount++;
                        if (logDetails && show is not null)
                        {
                            _logger.Info(
                                $"qBittorrent state updated for {show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}: Hash={torrent.Hash}, State={torrent.State}, Progress={torrent.ProgressDisplay}",
                                Common.LogTarget.All);
                        }

                        continue;
                    }

                    _trackedShowService.MarkTorrentRemoved(episode.Id, episode.TorrentHash!);
                    episodeRemovedCount++;
                    if (logDetails && show is not null)
                    {
                        _logger.Warning(
                            $"Mapped torrent is no longer present in qBittorrent for {show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}: Hash={episode.TorrentHash}",
                            Common.LogTarget.All);
                    }
                }

                foreach (var season in _trackedShowService.GetSeasons(trackedShowId)
                             .Where(season => season.SelectedPackOwnerSeasonNumber == season.SeasonNumber &&
                                              !string.IsNullOrWhiteSpace(season.PackTorrentHash)))
                {
                    if (torrentsByHash.TryGetValue(season.PackTorrentHash!, out var torrent))
                    {
                        _trackedShowService.UpdateSeasonPackTorrent(trackedShowId, season.SeasonNumber, torrent);
                        packUpdatedCount++;
                        if (logDetails && show is not null)
                        {
                            _logger.Info(
                                $"qBittorrent state updated for {show.DisplayTitle} S{season.SeasonNumber:00} pack: Hash={torrent.Hash}, State={torrent.State}, Progress={torrent.ProgressDisplay}",
                                Common.LogTarget.All);
                        }

                        continue;
                    }

                    _trackedShowService.MarkSeasonPackTorrentRemoved(trackedShowId, season.SeasonNumber, season.PackTorrentHash!);
                    packRemovedCount++;
                    if (logDetails && show is not null)
                    {
                        _logger.Warning(
                            $"Mapped pack torrent is no longer present in qBittorrent for {show.DisplayTitle} S{season.SeasonNumber:00}: Hash={season.PackTorrentHash}",
                            Common.LogTarget.All);
                    }
                }
            }

            return new ShowQbittorrentRefreshResult(
                Skipped: false,
                episodeUpdatedCount,
                packUpdatedCount,
                episodeRemovedCount,
                packRemovedCount);
        }
        finally
        {
            _isQbittorrentRefreshRunning = false;
        }
    }

    private void ReloadShowCards(long? expandedShowId = null, int? selectedPackSeasonNumber = null)
    {
        var existingExpandedIds = ShowCards
            .Where(card => card.IsExpanded)
            .Select(card => card.Id)
            .ToHashSet();
        var existingExpandedSeasonKeys = ShowCards
            .SelectMany(card => card.Seasons
                .Where(season => season.IsExpanded)
                .Select(season => (card.Id, season.SeasonNumber)))
            .ToHashSet();

        ShowCards.Clear();
        foreach (var show in _trackedShowService.GetShows())
        {
            var linkedEpisodeStatuses = GetLinkedEpisodeStatuses(show.TmdbId);
            var episodes = _trackedShowService.GetEpisodes(show.Id)
                .Select(episode => new TrackedEpisodeRowViewModel(episode, UpdateWanted))
                .ToList();
            foreach (var episode in episodes)
            {
                episode.LibraryLinkStatus = linkedEpisodeStatuses.TryGetValue((episode.SeasonNumber, episode.EpisodeNumber), out var status)
                    ? status
                    : "Not linked";
            }

            var seasonRecords = _trackedShowService.GetSeasons(show.Id)
                .ToDictionary(season => season.SeasonNumber);
            var downloadFolderOptions = GetDownloadFolderOptions();
            foreach (var episode in episodes)
            {
                if (_fetchJobService.TryGetCandidates(episode.Id, out var candidates))
                {
                    episode.ReplaceCandidates(candidates);
                }
            }

            var seasons = episodes
                .GroupBy(episode => episode.SeasonNumber)
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    seasonRecords.TryGetValue(group.Key, out var seasonRecord);
                    var season = new TrackedSeasonViewModel(
                        show.Id,
                        group.Key,
                        group,
                        downloadFolderOptions,
                        seasonRecord?.DownloadFolder ?? GetDefaultDownloadFolder(),
                        UpdateSeasonDownloadFolder,
                        UpdateSeasonManagementMode,
                        seasonRecord)
                    {
                        IsExpanded = existingExpandedSeasonKeys.Contains((show.Id, group.Key))
                    };
                    if (_fetchJobService.TryGetPackCandidates(show.Id, group.Key, out var packCandidates))
                    {
                        season.ReplacePackCandidates(packCandidates);
                    }

                    ApplyPackLinkCounts(season, episodes, linkedEpisodeStatuses);
                    return season;
                });
            var card = new TrackedShowCardViewModel(show, seasons, UpdateShowPreferences)
            {
                IsExpanded = expandedShowId == show.Id || existingExpandedIds.Contains(show.Id)
            };
            if (selectedPackSeasonNumber is not null && card.Id == expandedShowId)
            {
                card.SelectedPackSeason = card.Seasons.FirstOrDefault(season => season.SeasonNumber == selectedPackSeasonNumber.Value);
            }
            ShowCards.Add(card);
        }
    }

    private TrackedSeasonViewModel? FindPackOwnerSeason(TrackedSeasonViewModel season)
    {
        var ownerSeasonNumber = season.SelectedPackOwnerSeasonNumber ?? season.SeasonNumber;
        return ShowCards
            .FirstOrDefault(card => card.Id == season.ShowId)?
            .Seasons
            .FirstOrDefault(item => item.SeasonNumber == ownerSeasonNumber);
    }

    private void ReloadMovieCards(long? expandedMovieId = null)
    {
        MovieCards.Clear();
        foreach (var movie in _trackedMovieService.GetMovies())
        {
            var card = new TrackedMovieCardViewModel(movie, UpdateMovieWanted, UpdateMoviePreferences);
            card.LibraryLinkStatus = IsMovieLinked(movie.TmdbId) ? "Linked" : "Not linked";
            if (_fetchJobService.TryGetMovieCandidates(movie.Id, out var candidates))
            {
                card.ReplaceCandidates(candidates);
            }

            MovieCards.Add(card);
        }
    }

    private Dictionary<(int SeasonNumber, int EpisodeNumber), string> GetLinkedEpisodeStatuses(int tmdbId)
    {
        var providerId = tmdbId.ToString();
        var statuses = new Dictionary<(int SeasonNumber, int EpisodeNumber), string>();
        foreach (var item in _databaseService.GetSourceItems()
            .Where(item =>
                item.MediaKind == Common.MediaKind.TvEpisode &&
                item.MatchAccepted &&
                item.State == ItemState.Linked &&
                !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                File.Exists(item.LinkedPath) &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)))
        {
            var season = item.MappedSeasonNumber ?? item.SeasonNumber;
            var episode = item.MappedEpisodeNumber ?? item.EpisodeNumber;
            if (season is null || episode is null)
            {
                continue;
            }

            var key = (season.Value, episode.Value);
            var status = item.AutoTorrentLinkKind switch
            {
                Common.AutoTorrentLinkKind.SeasonPack when item.AutoTorrentPackOwnerSeasonNumber is not null =>
                    $"Linked by pack S{item.AutoTorrentPackOwnerSeasonNumber.Value:00}",
                Common.AutoTorrentLinkKind.Episode => "Linked by episode torrent",
                _ => "Linked"
            };

            if (!statuses.TryGetValue(key, out var existingStatus) ||
                GetLinkStatusPriority(status) > GetLinkStatusPriority(existingStatus))
            {
                statuses[key] = status;
            }
        }

        return statuses;
    }

    private static int GetLinkStatusPriority(string status)
    {
        if (status.StartsWith("Linked by pack", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return string.Equals(status, "Linked by episode torrent", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    }

    private static void ApplyPackLinkCounts(
        TrackedSeasonViewModel season,
        IReadOnlyList<TrackedEpisodeRowViewModel> episodes,
        IReadOnlyDictionary<(int SeasonNumber, int EpisodeNumber), string> linkedEpisodeStatuses)
    {
        if (!season.HasSavedPack || season.SelectedPackOwnerSeasonNumber is null)
        {
            season.PackLinkedEpisodeCount = 0;
            season.PackTotalEpisodeCount = 0;
            return;
        }

        var ownerSeasonNumber = season.SelectedPackOwnerSeasonNumber.Value;
        var coveredSeasons = ParseCoveredSeasons(season.SelectedPackCoveredSeasons).ToHashSet();
        if (coveredSeasons.Count == 0)
        {
            coveredSeasons.Add(ownerSeasonNumber);
        }

        var expectedPackStatus = $"Linked by pack S{ownerSeasonNumber:00}";
        var coveredEpisodes = episodes
            .Where(episode => coveredSeasons.Contains(episode.SeasonNumber))
            .ToList();

        season.PackTotalEpisodeCount = coveredEpisodes.Count;
        season.PackLinkedEpisodeCount = coveredEpisodes.Count(episode =>
            linkedEpisodeStatuses.TryGetValue((episode.SeasonNumber, episode.EpisodeNumber), out var status) &&
            string.Equals(status, expectedPackStatus, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<int> ParseCoveredSeasons(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.TryParse(item, out var season) ? season : 0)
            .Where(season => season > 0)
            .Distinct()
            .Order()
            .ToList();
    }

    private bool IsMovieLinked(int tmdbId)
    {
        var providerId = tmdbId.ToString();
        return _databaseService.GetSourceItems().Any(item =>
            item.MediaKind == Common.MediaKind.Movie &&
            item.MatchAccepted &&
            item.State == ItemState.Linked &&
            !string.IsNullOrWhiteSpace(item.LinkedPath) &&
            File.Exists(item.LinkedPath) &&
            string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    private void ReloadFetchJobs()
    {
        var selectedJobId = SelectedFetchJob?.Id;
        FetchJobs.Clear();
        foreach (var job in _fetchJobService.GetJobs())
        {
            FetchJobs.Add(new FetchJobRowViewModel(job));
        }

        SelectedFetchJob = selectedJobId is null
            ? FetchJobs.FirstOrDefault()
            : FetchJobs.FirstOrDefault(job => job.Id == selectedJobId.Value) ?? FetchJobs.FirstOrDefault();
    }

    private void UpdateWanted(long episodeId, bool isWanted)
    {
        _trackedShowService.UpdateWanted(episodeId, isWanted);
    }

    private void UpdateShowPreferences(long showId, string preferredQuality, string preferredAudioCodec, int minimumSeeders)
    {
        _trackedShowService.UpdatePreferences(showId, preferredQuality, preferredAudioCodec, minimumSeeders);
    }

    private void UpdateSeasonDownloadFolder(long showId, int seasonNumber, string? downloadFolder)
    {
        _trackedShowService.UpdateSeasonDownloadFolder(showId, seasonNumber, downloadFolder);
        RefreshStorageStatus(updateStatusMessage: false);
    }

    private void UpdateSeasonManagementMode(long showId, int seasonNumber, Common.SeasonManagementMode mode)
    {
        _trackedShowService.UpdateSeasonPackMode(showId, seasonNumber, mode);
    }

    private void UpdateMovieWanted(long movieId, bool isWanted)
    {
        _trackedMovieService.UpdateWanted(movieId, isWanted);
    }

    private void UpdateMoviePreferences(long movieId, string preferredQuality, string preferredAudioCodec, int minimumSeeders)
    {
        _trackedMovieService.UpdatePreferences(movieId, preferredQuality, preferredAudioCodec, minimumSeeders);
    }

    private string GetDefaultDownloadFolder()
    {
        var configuredFolder = _settingsService.Current.AutoTorrent.DownloadFolder;
        if (!string.IsNullOrWhiteSpace(configuredFolder))
        {
            return configuredFolder;
        }

        return _settingsService.Current.SourceFolders.FirstOrDefault() ?? string.Empty;
    }

    private string GetEpisodeDownloadFolder(TrackedEpisodeRowViewModel episode)
    {
        return string.IsNullOrWhiteSpace(episode.DownloadFolder) ? GetDefaultDownloadFolder() : episode.DownloadFolder;
    }

    private string GetSeasonDownloadFolder(TrackedSeasonViewModel season)
    {
        return string.IsNullOrWhiteSpace(season.SelectedSeasonDownloadFolder)
            ? GetDefaultDownloadFolder()
            : season.SelectedSeasonDownloadFolder;
    }

    private IReadOnlyList<string> GetDownloadFolderOptions()
    {
        return GetKnownDownloadFolders().ToList();
    }

    private IEnumerable<string> GetKnownDownloadFolders()
    {
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(_settingsService.Current.AutoTorrent.DownloadFolder))
        {
            folders.Add(_settingsService.Current.AutoTorrent.DownloadFolder);
        }

        folders.AddRange(_settingsService.Current.AutoTorrent.DownloadFolders);
        folders.AddRange(_settingsService.Current.SourceFolders);
        return folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void OnFetchJobsChanged(object? sender, EventArgs e)
    {
        Dispatch(ReloadFetchJobs);
    }

    private void OnCandidatesChanged(object? sender, EventArgs e)
    {
        Dispatch(RefreshCandidatesInPlace);
    }

    private void RefreshCandidatesInPlace()
    {
        foreach (var episode in ShowCards
                     .SelectMany(card => card.Seasons)
                     .SelectMany(season => season.Episodes))
        {
            if (_fetchJobService.TryGetCandidates(episode.Id, out var candidates))
            {
                episode.ReplaceCandidates(candidates);
            }
        }

        foreach (var season in ShowCards.SelectMany(card => card.Seasons))
        {
            if (_fetchJobService.TryGetPackCandidates(season.ShowId, season.SeasonNumber, out var packCandidates))
            {
                season.ReplacePackCandidates(packCandidates);
            }

            season.NotifyStatsChanged();
        }

        foreach (var movie in MovieCards)
        {
            if (_fetchJobService.TryGetMovieCandidates(movie.Id, out var candidates))
            {
                movie.ReplaceCandidates(candidates);
            }
        }
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private sealed record ShowQbittorrentRefreshResult(
        bool Skipped,
        int EpisodeUpdatedCount,
        int PackUpdatedCount,
        int EpisodeRemovedCount,
        int PackRemovedCount)
    {
        public int UpdatedCount => EpisodeUpdatedCount + PackUpdatedCount;

        public int RemovedCount => EpisodeRemovedCount + PackRemovedCount;
    }
}
