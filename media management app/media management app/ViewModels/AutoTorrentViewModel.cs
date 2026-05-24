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
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _storageStatusTimer;

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

    public AutoTorrentViewModel(
        ISettingsService settingsService,
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        IFetchJobService fetchJobService,
        IQbittorrentClient qbittorrentClient,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _fetchJobService = fetchJobService;
        _qbittorrentClient = qbittorrentClient;
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
    private void SaveSelectedCandidates(TrackedShowCardViewModel? card)
    {
        if (card is null)
        {
            StatusMessage = "Select a show first.";
            return;
        }

        var savedCount = 0;
        foreach (var episode in card.Seasons.SelectMany(season => season.Episodes))
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
            _trackedShowService.RefreshAvailability();
            var torrents = await _qbittorrentClient.GetTorrentsAsync();
            var torrentsByHash = torrents.ToDictionary(torrent => torrent.Hash, StringComparer.OrdinalIgnoreCase);
            var updatedCount = 0;
            var removedCount = 0;
            foreach (var episode in ShowCards.SelectMany(card => card.Seasons).SelectMany(season => season.Episodes))
            {
                if (string.IsNullOrWhiteSpace(episode.TorrentHash))
                {
                    continue;
                }

                if (torrentsByHash.TryGetValue(episode.TorrentHash, out var torrent))
                {
                    _trackedShowService.UpdateTorrentState(episode.Id, torrent);
                    episode.UpdateTorrentStatus(torrent);
                    updatedCount++;
                    continue;
                }

                _trackedShowService.MarkTorrentRemoved(episode.Id, episode.TorrentHash);
                episode.MarkTorrentRemoved();
                removedCount++;
            }

            ReloadShowCards();
            StatusMessage = $"Refreshed availability and qBittorrent state: {updatedCount} tracked, {removedCount} removed.";
        });
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
            _trackedShowService.RefreshAvailability(card.Id);
            var torrents = await _qbittorrentClient.GetTorrentsAsync();
            var torrentsByHash = torrents.ToDictionary(torrent => torrent.Hash, StringComparer.OrdinalIgnoreCase);
            var trackedEpisodes = card.Seasons
                .SelectMany(season => season.Episodes)
                .Where(episode => !string.IsNullOrWhiteSpace(episode.TorrentHash))
                .ToList();
            var updatedCount = 0;
            var removedCount = 0;

            foreach (var episode in trackedEpisodes)
            {
                if (torrentsByHash.TryGetValue(episode.TorrentHash, out var torrent))
                {
                    _trackedShowService.UpdateTorrentState(episode.Id, torrent);
                    episode.UpdateTorrentStatus(torrent);
                    updatedCount++;
                    _logger.Info(
                        $"qBittorrent state updated for {card.Title} {episode.EpisodeCode}: Hash={torrent.Hash}, State={torrent.State}, Progress={torrent.ProgressDisplay}",
                        Common.LogTarget.All);
                    continue;
                }

                _trackedShowService.MarkTorrentRemoved(episode.Id, episode.TorrentHash);
                episode.MarkTorrentRemoved();
                removedCount++;
                _logger.Warning(
                    $"Mapped torrent is no longer present in qBittorrent for {card.Title} {episode.EpisodeCode}: Hash={episode.TorrentHash}",
                    Common.LogTarget.All);
            }

            ReloadShowCards(card.Id);
            StatusMessage = $"Updated qBittorrent for {card.Title}: {updatedCount} tracked, {removedCount} removed.";
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

    private void ReloadShowCards(long? expandedShowId = null)
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
            var episodes = _trackedShowService.GetEpisodes(show.Id)
                .Select(episode => new TrackedEpisodeRowViewModel(episode, UpdateWanted))
                .ToList();
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
                        UpdateSeasonDownloadFolder)
                    {
                        IsExpanded = existingExpandedSeasonKeys.Contains((show.Id, group.Key))
                    };
                    return season;
                });
            var card = new TrackedShowCardViewModel(show, seasons, UpdateShowPreferences)
            {
                IsExpanded = expandedShowId == show.Id || existingExpandedIds.Contains(show.Id)
            };
            ShowCards.Add(card);
        }
    }

    private void ReloadMovieCards(long? expandedMovieId = null)
    {
        MovieCards.Clear();
        foreach (var movie in _trackedMovieService.GetMovies())
        {
            var card = new TrackedMovieCardViewModel(movie, UpdateMovieWanted, UpdateMoviePreferences);
            if (_fetchJobService.TryGetMovieCandidates(movie.Id, out var candidates))
            {
                card.ReplaceCandidates(candidates);
            }

            MovieCards.Add(card);
        }
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
}
