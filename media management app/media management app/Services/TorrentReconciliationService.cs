using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface ITorrentReconciliationService
{
    event EventHandler? Reconciled;

    Task<TorrentReconciliationResult> ReconcileAsync(
        TorrentReconciliationScope scope,
        CancellationToken cancellationToken = default,
        IReadOnlyList<SourceItem>? sourceItems = null);
}

public sealed class TorrentReconciliationService : ITorrentReconciliationService
{
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IDatabaseService _databaseService;
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly IAutoTorrentLinkService _autoTorrentLinkService;
    private readonly IPackLinkCoordinatorService _packLinkCoordinatorService;
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;

    public event EventHandler? Reconciled;

    public TorrentReconciliationService(
        IQbittorrentClient qbittorrentClient,
        IDatabaseService databaseService,
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        IAutoTorrentLinkService autoTorrentLinkService,
        IPackLinkCoordinatorService packLinkCoordinatorService,
        ISettingsService settingsService,
        IAppLogger logger)
    {
        _qbittorrentClient = qbittorrentClient;
        _databaseService = databaseService;
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _autoTorrentLinkService = autoTorrentLinkService;
        _packLinkCoordinatorService = packLinkCoordinatorService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<TorrentReconciliationResult> ReconcileAsync(
        TorrentReconciliationScope scope,
        CancellationToken cancellationToken = default,
        IReadOnlyList<SourceItem>? sourceItems = null)
    {
        var result = new TorrentReconciliationResult();
        var torrents = await _qbittorrentClient.GetTorrentsAsync(cancellationToken);
        var torrentsByHash = torrents
            .Where(torrent => !string.IsNullOrWhiteSpace(torrent.Hash))
            .GroupBy(torrent => torrent.Hash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var shows = _trackedShowService.GetShows()
            .Where(show => scope.Includes(MediaKind.TvEpisode, show.Id))
            .ToList();
        var movies = _trackedMovieService.GetMovies()
            .Where(movie => scope.Includes(MediaKind.Movie, movie.Id))
            .ToList();
        var allOrders = _databaseService.GetTorrentCartOrders()
            .Where(order => scope.Includes(order.TargetKind, order.MediaId))
            .ToList();

        var episodesById = LoadEpisodesById(shows);
        var seasons = LoadPackSeasons(shows);

        var usedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await RefreshSavedEpisodeHashesAsync(episodesById.Values, torrentsByHash, usedHashes, result, cancellationToken);
        await RefreshSavedSeasonPackHashesAsync(seasons, torrentsByHash, usedHashes, result, cancellationToken);
        await RefreshSavedMovieHashesAsync(movies, torrentsByHash, usedHashes, result, cancellationToken);
        RefreshSavedOrderHashes(allOrders, torrentsByHash, usedHashes, result);

        await AdoptManualTorrentsAsync(torrents, usedHashes, episodesById.Values.ToList(), seasons, movies, result, cancellationToken);
        episodesById = LoadEpisodesById(shows);
        seasons = LoadPackSeasons(shows);
        movies = _trackedMovieService.GetMovies()
            .Where(movie => scope.Includes(MediaKind.Movie, movie.Id))
            .ToList();
        SyncOrdersFromTrackedItems(allOrders, episodesById, seasons, movies, torrentsByHash, result);

        sourceItems ??= _databaseService.GetSourceItems();
        RefreshAvailabilityForScope(scope, sourceItems);
        _logger.Info($"Torrent reconciliation complete. {result.Summary}", LogTarget.All);
        Reconciled?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private void RefreshAvailabilityForScope(TorrentReconciliationScope scope, IReadOnlyList<SourceItem> sourceItems)
    {
        if (scope.MediaKind is null)
        {
            _trackedShowService.RefreshAvailability(sourceItems);
            _trackedMovieService.RefreshAvailability(sourceItems);
            return;
        }

        if (scope.MediaKind == MediaKind.TvEpisode && scope.MediaId is not null)
        {
            _trackedShowService.RefreshAvailability(scope.MediaId.Value, sourceItems);
            return;
        }

        if (scope.MediaKind == MediaKind.Movie && scope.MediaId is not null)
        {
            _trackedMovieService.RefreshAvailability(scope.MediaId.Value, sourceItems);
        }
    }

    private Dictionary<long, (TrackedShow Show, TrackedEpisode Episode)> LoadEpisodesById(IEnumerable<TrackedShow> shows)
    {
        var episodesById = new Dictionary<long, (TrackedShow Show, TrackedEpisode Episode)>();
        foreach (var show in shows)
        {
            foreach (var episode in _trackedShowService.GetEpisodes(show.Id))
            {
                episodesById[episode.Id] = (show, episode);
            }
        }

        return episodesById;
    }

    private List<(TrackedShow Show, TrackedSeason Season)> LoadPackSeasons(IEnumerable<TrackedShow> shows)
    {
        return shows
            .SelectMany(show => _trackedShowService.GetSeasons(show.Id)
                .Where(season => season.ManagementMode == SeasonManagementMode.Pack)
                .Select(season => (show, season)))
            .ToList();
    }

    private async Task RefreshSavedEpisodeHashesAsync(
        IEnumerable<(TrackedShow Show, TrackedEpisode Episode)> episodes,
        IReadOnlyDictionary<string, AddedTorrentResult> torrentsByHash,
        HashSet<string> usedHashes,
        TorrentReconciliationResult result,
        CancellationToken cancellationToken)
    {
        foreach (var (show, episode) in episodes.Where(item => !string.IsNullOrWhiteSpace(item.Episode.TorrentHash)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (torrentsByHash.TryGetValue(episode.TorrentHash!, out var torrent))
            {
                _trackedShowService.UpdateTorrentState(episode.Id, torrent);
                usedHashes.Add(torrent.Hash);
                CountMatched(torrent, result);
                await AutoLinkEpisodeIfConfiguredAsync(show, episode, torrent, result, cancellationToken);
                continue;
            }

            _trackedShowService.MarkTorrentRemoved(episode.Id, episode.TorrentHash!);
            result.MissingCount++;
        }
    }

    private async Task RefreshSavedSeasonPackHashesAsync(
        IEnumerable<(TrackedShow Show, TrackedSeason Season)> seasons,
        IReadOnlyDictionary<string, AddedTorrentResult> torrentsByHash,
        HashSet<string> usedHashes,
        TorrentReconciliationResult result,
        CancellationToken cancellationToken)
    {
        foreach (var (show, season) in seasons.Where(item => !string.IsNullOrWhiteSpace(item.Season.PackTorrentHash)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (torrentsByHash.TryGetValue(season.PackTorrentHash!, out var torrent))
            {
                var previousProgress = season.PackTorrentProgress;
                var ownerSeasonNumber = season.SelectedPackOwnerSeasonNumber ?? season.SeasonNumber;
                _trackedShowService.UpdateSeasonPackTorrent(show.Id, ownerSeasonNumber, torrent);
                usedHashes.Add(torrent.Hash);
                CountMatched(torrent, result);
                await _packLinkCoordinatorService.TryAutoReconcilePackAsync(
                    show,
                    season,
                    torrent,
                    previousProgress,
                    result,
                    cancellationToken);
                continue;
            }

            _trackedShowService.MarkSeasonPackTorrentRemoved(show.Id, season.SeasonNumber, season.PackTorrentHash!);
            result.MissingCount++;
        }
    }

    private async Task RefreshSavedMovieHashesAsync(
        IEnumerable<TrackedMovie> movies,
        IReadOnlyDictionary<string, AddedTorrentResult> torrentsByHash,
        HashSet<string> usedHashes,
        TorrentReconciliationResult result,
        CancellationToken cancellationToken)
    {
        foreach (var movie in movies.Where(movie => !string.IsNullOrWhiteSpace(movie.TorrentHash)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (torrentsByHash.TryGetValue(movie.TorrentHash!, out var torrent))
            {
                _trackedMovieService.UpdateTorrentState(movie.Id, torrent);
                usedHashes.Add(torrent.Hash);
                CountMatched(torrent, result);
                await AutoLinkMovieIfConfiguredAsync(movie, torrent, result, cancellationToken);
                continue;
            }

            _trackedMovieService.MarkTorrentRemoved(movie.Id, movie.TorrentHash!);
            result.MissingCount++;
        }
    }

    private void RefreshSavedOrderHashes(
        IEnumerable<TorrentCartOrder> orders,
        IReadOnlyDictionary<string, AddedTorrentResult> torrentsByHash,
        HashSet<string> usedHashes,
        TorrentReconciliationResult result)
    {
        foreach (var order in orders.Where(order => !string.IsNullOrWhiteSpace(order.TorrentHash)))
        {
            if (torrentsByHash.TryGetValue(order.TorrentHash, out var torrent))
            {
                ApplyTorrentToOrder(order, torrent);
                usedHashes.Add(torrent.Hash);
                CountMatched(torrent, result);
                continue;
            }

            order.Status = TorrentOrderStatus.Failed;
            order.StatusDetail = "Torrent missing from qBittorrent.";
            _databaseService.UpsertTorrentCartOrder(order);
            result.MissingCount++;
        }
    }

    private async Task AdoptManualTorrentsAsync(
        IReadOnlyList<AddedTorrentResult> torrents,
        HashSet<string> usedHashes,
        IReadOnlyList<(TrackedShow Show, TrackedEpisode Episode)> episodes,
        IReadOnlyList<(TrackedShow Show, TrackedSeason Season)> seasons,
        IReadOnlyList<TrackedMovie> movies,
        TorrentReconciliationResult result,
        CancellationToken cancellationToken)
    {
        foreach (var torrent in torrents.Where(torrent => !usedHashes.Contains(torrent.Hash)))
        {
            var matches = new List<Func<Task>>();

            foreach (var (show, episode) in episodes.Where(item => string.IsNullOrWhiteSpace(item.Episode.TorrentHash)))
            {
                if (MatchesEpisode(torrent.Name, show, episode))
                {
                    matches.Add(() =>
                    {
                        _trackedShowService.UpdateTorrentState(episode.Id, torrent);
                        CountMatched(torrent, result);
                        return AutoLinkEpisodeIfConfiguredAsync(show, episode, torrent, result, cancellationToken);
                    });
                }
            }

            foreach (var (show, season) in seasons.Where(item => string.IsNullOrWhiteSpace(item.Season.PackTorrentHash)))
            {
                if (MatchesSeasonPack(torrent.Name, show, season))
                {
                    matches.Add(async () =>
                    {
                        _trackedShowService.UpdateSeasonPackTorrent(show.Id, season.SeasonNumber, torrent);
                        CountMatched(torrent, result);
                        await _packLinkCoordinatorService.TryAutoReconcilePackAsync(
                            show,
                            season,
                            torrent,
                            previousProgress: 0,
                            result,
                            cancellationToken);
                    });
                }
            }

            foreach (var movie in movies.Where(movie => string.IsNullOrWhiteSpace(movie.TorrentHash)))
            {
                if (MatchesMovie(torrent.Name, movie))
                {
                    matches.Add(() =>
                    {
                        _trackedMovieService.UpdateTorrentState(movie.Id, torrent);
                        CountMatched(torrent, result);
                        return AutoLinkMovieIfConfiguredAsync(movie, torrent, result, cancellationToken);
                    });
                }
            }

            if (matches.Count == 1)
            {
                await matches[0]();
                usedHashes.Add(torrent.Hash);
            }
        }
    }

    private void SyncOrdersFromTrackedItems(
        IEnumerable<TorrentCartOrder> orders,
        IReadOnlyDictionary<long, (TrackedShow Show, TrackedEpisode Episode)> episodesById,
        IReadOnlyList<(TrackedShow Show, TrackedSeason Season)> seasons,
        IReadOnlyList<TrackedMovie> movies,
        IReadOnlyDictionary<string, AddedTorrentResult> torrentsByHash,
        TorrentReconciliationResult result)
    {
        foreach (var order in orders.Where(order => string.IsNullOrWhiteSpace(order.TorrentHash)))
        {
            var torrent = ResolveTorrentForOrder(order, episodesById, seasons, movies, torrentsByHash);
            if (torrent is null)
            {
                continue;
            }

            ApplyTorrentToOrder(order, torrent);
            CountMatched(torrent, result);
        }
    }

    private static AddedTorrentResult? ResolveTorrentForOrder(
        TorrentCartOrder order,
        IReadOnlyDictionary<long, (TrackedShow Show, TrackedEpisode Episode)> episodesById,
        IReadOnlyList<(TrackedShow Show, TrackedSeason Season)> seasons,
        IReadOnlyList<TrackedMovie> movies,
        IReadOnlyDictionary<string, AddedTorrentResult> torrentsByHash)
    {
        if (order.TargetKind == MediaKind.Movie)
        {
            var movie = movies.FirstOrDefault(movie => movie.Id == order.MediaId);
            return !string.IsNullOrWhiteSpace(movie?.TorrentHash) &&
                   torrentsByHash.TryGetValue(movie.TorrentHash, out var torrent)
                ? torrent
                : null;
        }

        if (order.EpisodeId is not null &&
            episodesById.TryGetValue(order.EpisodeId.Value, out var episodeMatch) &&
            !string.IsNullOrWhiteSpace(episodeMatch.Episode.TorrentHash) &&
            torrentsByHash.TryGetValue(episodeMatch.Episode.TorrentHash, out var episodeTorrent))
        {
            return episodeTorrent;
        }

        if (order.EpisodeId is null && order.SeasonNumber is not null)
        {
            var season = seasons.FirstOrDefault(item =>
                item.Show.Id == order.MediaId &&
                item.Season.SeasonNumber == order.SeasonNumber.Value);
            if (!string.IsNullOrWhiteSpace(season.Season?.PackTorrentHash) &&
                torrentsByHash.TryGetValue(season.Season.PackTorrentHash, out var seasonTorrent))
            {
                return seasonTorrent;
            }
        }

        return null;
    }

    private void ApplyTorrentToOrder(TorrentCartOrder order, AddedTorrentResult torrent)
    {
        order.TorrentHash = torrent.Hash;
        order.TorrentName = torrent.Name;
        order.TorrentState = torrent.State;
        order.TorrentProgress = torrent.Progress;
        order.Status = torrent.IsComplete ? TorrentOrderStatus.Completed : TorrentOrderStatus.Downloading;
        order.StatusDetail = torrent.IsComplete
            ? $"Ready to link: {torrent.Name}"
            : $"Downloading: {torrent.Name} ({torrent.ProgressDisplay})";
        _databaseService.UpsertTorrentCartOrder(order);
    }

    private async Task AutoLinkEpisodeIfConfiguredAsync(
        TrackedShow show,
        TrackedEpisode episode,
        AddedTorrentResult torrent,
        TorrentReconciliationResult result,
        CancellationToken cancellationToken)
    {
        if (show.IsAutoTracked && !show.AutoTrackAutoReconcileAndLink)
        {
            return;
        }

        if (!ShouldAutoLinkCompleted(show, torrent))
        {
            return;
        }

        var linkResult = await _autoTorrentLinkService.LinkEpisodeAsync(
            show.Id,
            episode.SeasonNumber,
            episode.EpisodeNumber,
            cancellationToken);
        result.LinkedCount += linkResult.LinkedCount;
        if (linkResult.LinkedCount == 0 && linkResult.ErrorCount > 0)
        {
            _logger.Warning(
                $"Auto-link failed for '{show.DisplayTitle}' S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}: {string.Join("; ", linkResult.Messages)}",
                LogTarget.All);
        }
    }

    private async Task AutoLinkMovieIfConfiguredAsync(
        TrackedMovie movie,
        AddedTorrentResult torrent,
        TorrentReconciliationResult result,
        CancellationToken cancellationToken)
    {
        if (!torrent.IsComplete)
        {
            return;
        }

        if (!_settingsService.Current.AutoTorrent.AutoLinkCompletedDownloads)
        {
            return;
        }

        var linkResult = await _autoTorrentLinkService.LinkMovieAsync(movie.Id, cancellationToken);
        result.LinkedCount += linkResult.LinkedCount;
    }

    private bool ShouldAutoLinkCompleted(TrackedShow show, AddedTorrentResult torrent)
    {
        if (!torrent.IsComplete)
        {
            return false;
        }

        if (show.IsAutoTracked && show.AutoTrackAutoReconcileAndLink)
        {
            return true;
        }

        return _settingsService.Current.AutoTorrent.AutoLinkCompletedDownloads;
    }

    private static bool MatchesEpisode(string torrentName, TrackedShow show, TrackedEpisode episode)
    {
        if (EqualsNormalized(torrentName, episode.SelectedCandidateName))
        {
            return true;
        }

        return ContainsNormalized(torrentName, show.Title) &&
               (ContainsToken(torrentName, $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}") ||
                ContainsNumberToken(torrentName, episode.EpisodeNumber));
    }

    private static bool MatchesSeasonPack(string torrentName, TrackedShow show, TrackedSeason season)
    {
        if (EqualsNormalized(torrentName, season.SelectedPackCandidateName))
        {
            return true;
        }

        return ContainsNormalized(torrentName, show.Title) &&
               (ContainsToken(torrentName, $"S{season.SeasonNumber:00}") ||
                ContainsToken(torrentName, $"Season {season.SeasonNumber}") ||
                ContainsToken(torrentName, $"Season {season.SeasonNumber:00}")) &&
               Regex.IsMatch(torrentName, "\\b(pack|batch|season)\\b", RegexOptions.IgnoreCase);
    }

    private static bool MatchesMovie(string torrentName, TrackedMovie movie)
    {
        if (EqualsNormalized(torrentName, movie.SelectedCandidateName))
        {
            return true;
        }

        return ContainsNormalized(torrentName, movie.Title) &&
               (movie.ReleaseYear is null || ContainsNumberToken(torrentName, movie.ReleaseYear.Value));
    }

    private static void CountMatched(AddedTorrentResult torrent, TorrentReconciliationResult result)
    {
        result.MatchedCount++;
        if (torrent.IsComplete)
        {
            result.CompletedCount++;
        }
    }

    private static bool EqualsNormalized(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsNormalized(string source, string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               Normalize(source).Contains(Normalize(value), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsToken(string source, string token)
    {
        return Normalize(source).Contains(Normalize(token), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsNumberToken(string source, int value)
    {
        return Regex.IsMatch(source, $@"(?<!\d){Regex.Escape(value.ToString())}(?!\d)");
    }

    private static string Normalize(string value)
    {
        return Regex.Replace(value, "[^a-zA-Z0-9]+", " ").Trim();
    }
}
