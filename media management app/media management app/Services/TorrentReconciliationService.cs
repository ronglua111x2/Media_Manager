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

        // Torrents still awaiting add-gate validation (live or interrupted by a crash) are never adopted by name.
        var usedHashes = InterruptedOrderRecovery.CollectPendingValidationHashes(_databaseService.GetTorrentCartOrders());
        // A torrent already tracked by any show, season, movie, order or link is never adopted for
        // something else, also when this pass is scoped to one show (PARSE-25).
        usedHashes.UnionWith(CollectTrackedHashes());
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

        // Clear broken LinkedPath values and purge SourceItems where both files are gone.
        // This allows episodes to transition from Available to Missing when files were deleted externally.
        RefreshLinkStatusForScope(scope);
        PurgeOrphanedSourceItemsForScope(scope, torrentsByHash);

        sourceItems = _databaseService.GetSourceItems();
        RefreshAvailabilityForScope(scope, sourceItems);
        _logger.Info($"Torrent reconciliation complete. {result.Summary}", LogTarget.All);
        Reconciled?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private HashSet<string> CollectTrackedHashes()
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? hash)
        {
            if (!string.IsNullOrWhiteSpace(hash))
            {
                hashes.Add(hash);
            }
        }

        foreach (var show in _trackedShowService.GetShows())
        {
            foreach (var episode in _trackedShowService.GetEpisodes(show.Id))
            {
                Add(episode.TorrentHash);
            }

            foreach (var season in _trackedShowService.GetSeasons(show.Id))
            {
                Add(season.PackTorrentHash);
            }
        }

        foreach (var movie in _trackedMovieService.GetMovies())
        {
            Add(movie.TorrentHash);
        }

        foreach (var order in _databaseService.GetTorrentCartOrders())
        {
            Add(order.TorrentHash);
        }

        foreach (var item in _databaseService.GetSourceItems())
        {
            Add(item.AutoTorrentTorrentHash);
        }

        return hashes;
    }

    private void RefreshLinkStatusForScope(TorrentReconciliationScope scope)
    {
        if (scope.MediaKind == MediaKind.TvEpisode && scope.MediaId is not null)
        {
            _autoTorrentLinkService.RefreshLinkStatus(showId: scope.MediaId.Value);
            return;
        }

        if (scope.MediaKind == MediaKind.Movie && scope.MediaId is not null)
        {
            _autoTorrentLinkService.RefreshLinkStatus(movieId: scope.MediaId.Value);
            return;
        }

        // Global scope: clear all broken links
        _autoTorrentLinkService.RefreshLinkStatus();
    }

    private void PurgeOrphanedSourceItemsForScope(
        TorrentReconciliationScope scope,
        IReadOnlyDictionary<string, AddedTorrentResult> torrentsByHash)
    {
        var candidates = _databaseService.GetSourceItems().AsEnumerable();

        if (scope.MediaKind == MediaKind.TvEpisode && scope.MediaId is not null)
        {
            var providerId = _trackedShowService.GetShows()
                .FirstOrDefault(s => s.Id == scope.MediaId.Value)?.TmdbId.ToString();
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return;
            }

            candidates = candidates.Where(item =>
                item.MediaKind == MediaKind.TvEpisode &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        }
        else if (scope.MediaKind == MediaKind.Movie && scope.MediaId is not null)
        {
            var providerId = _trackedMovieService.GetMovies()
                .FirstOrDefault(m => m.Id == scope.MediaId.Value)?.TmdbId.ToString();
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return;
            }

            candidates = candidates.Where(item =>
                item.MediaKind == MediaKind.Movie &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        }

        var purgedCount = 0;
        var keptCount = 0;
        var storage = new LinkStorageProbe();
        foreach (var item in candidates.ToList())
        {
            if (File.Exists(item.FilePath) ||
                (!string.IsNullOrWhiteSpace(item.LinkedPath) && File.Exists(item.LinkedPath)))
            {
                continue;
            }

            // Missing files only mean "deleted" when their storage is reachable and qBittorrent
            // does not report the torrent's files as temporarily missing (PARSE-27).
            if (!storage.IsSourceStorageReachable(item) ||
                !storage.IsLinkStorageReachable(item) ||
                IsTorrentMissingFiles(item, torrentsByHash))
            {
                keptCount++;
                continue;
            }

            _databaseService.DeleteSourceItem(item.Id);
            purgedCount++;
        }

        if (purgedCount > 0)
        {
            _logger.Info($"Purged {purgedCount} orphaned source item(s) with missing files.", LogTarget.All);
        }

        if (keptCount > 0)
        {
            _logger.Warning(
                $"Kept {keptCount} source item(s) with missing files because their storage is not reachable " +
                $"({string.Join(", ", storage.UnreachableFolders)}) or qBittorrent reports missing files.",
                LogTarget.File | LogTarget.Console);
        }
    }

    /// <summary>
    /// True when the item's file belongs to a torrent that qBittorrent reports as "missingFiles".
    /// Matched by hash when the record still has one, otherwise by the torrent's content path
    /// (save path + torrent name), because a lost link clears the record's provenance.
    /// </summary>
    private static bool IsTorrentMissingFiles(
        SourceItem item,
        IReadOnlyDictionary<string, AddedTorrentResult> torrentsByHash)
    {
        if (!string.IsNullOrWhiteSpace(item.AutoTorrentTorrentHash) &&
            torrentsByHash.TryGetValue(item.AutoTorrentTorrentHash, out var byHash) &&
            IsMissingFilesState(byHash))
        {
            return true;
        }

        foreach (var torrent in torrentsByHash.Values.Where(IsMissingFilesState))
        {
            if (string.IsNullOrWhiteSpace(torrent.SavePath) || string.IsNullOrWhiteSpace(torrent.Name))
            {
                continue;
            }

            var contentPath = Path.Combine(torrent.SavePath, torrent.Name);
            if (string.Equals(item.FilePath, contentPath, StringComparison.OrdinalIgnoreCase) ||
                item.FilePath.StartsWith(contentPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMissingFilesState(AddedTorrentResult torrent) =>
        string.Equals(torrent.State, "missingFiles", StringComparison.OrdinalIgnoreCase);

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
        // The episode/season/movie lists are a snapshot taken before adoption; remember what this
        // pass adopted so a later torrent cannot overwrite it.
        var adoptedEpisodeIds = new HashSet<long>();
        var adoptedSeasons = new HashSet<(long ShowId, int SeasonNumber)>();
        var adoptedMovieIds = new HashSet<long>();

        foreach (var torrent in torrents.Where(torrent => !usedHashes.Contains(torrent.Hash)))
        {
            var matches = new List<Func<Task>>();

            foreach (var (show, episode) in episodes.Where(item =>
                         string.IsNullOrWhiteSpace(item.Episode.TorrentHash) &&
                         !adoptedEpisodeIds.Contains(item.Episode.Id)))
            {
                if (MatchesEpisode(torrent.Name, show, episode))
                {
                    matches.Add(() =>
                    {
                        adoptedEpisodeIds.Add(episode.Id);
                        _trackedShowService.UpdateTorrentState(episode.Id, torrent);
                        CountMatched(torrent, result);
                        return AutoLinkEpisodeIfConfiguredAsync(show, episode, torrent, result, cancellationToken);
                    });
                }
            }

            foreach (var (show, season) in seasons.Where(item =>
                         string.IsNullOrWhiteSpace(item.Season.PackTorrentHash) &&
                         !adoptedSeasons.Contains((item.Show.Id, item.Season.SeasonNumber))))
            {
                if (MatchesSeasonPack(torrent.Name, show, season))
                {
                    matches.Add(async () =>
                    {
                        adoptedSeasons.Add((show.Id, season.SeasonNumber));
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

            foreach (var movie in movies.Where(movie =>
                         string.IsNullOrWhiteSpace(movie.TorrentHash) &&
                         !adoptedMovieIds.Contains(movie.Id)))
            {
                if (MatchesMovie(torrent.Name, movie))
                {
                    matches.Add(() =>
                    {
                        adoptedMovieIds.Add(movie.Id);
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
        foreach (var order in orders.Where(order =>
                     string.IsNullOrWhiteSpace(order.TorrentHash) &&
                     string.IsNullOrWhiteSpace(order.PendingValidationHash)))
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
        order.TorrentState = QbittorrentTorrentStateNormalizer.Normalize(torrent.State, torrent.IsComplete);
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

        if (!ContainsNormalized(torrentName, show.Title))
        {
            return false;
        }

        // Only an explicit SxxEyy or a parsed episode number counts. A bare number anywhere in the
        // name (AAC2.0, DD 5 1, x265, a CRC) matched unrelated episodes before (PARSE-25).
        if (ContainsToken(torrentName, $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}"))
        {
            return true;
        }

        var parsed = TorrentCandidateParser.Parse(torrentName, allowAnimeAbsolute: true);
        if (parsed.EpisodeNumber != episode.EpisodeNumber)
        {
            return false;
        }

        // Absolute anime numbering carries no season; accept it for season 1 only.
        return parsed.SeasonNumber == episode.SeasonNumber ||
               (parsed.SeasonNumber is null && episode.SeasonNumber == 1);
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

    /// <summary>Whole-word containment: "Lara" no longer matches inside "Laramie" (PARSE-25).</summary>
    private static bool ContainsNormalized(string source, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalizedValue = Normalize(value);
        return normalizedValue.Length > 0 &&
               $" {Normalize(source)} ".Contains($" {normalizedValue} ", StringComparison.OrdinalIgnoreCase);
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
