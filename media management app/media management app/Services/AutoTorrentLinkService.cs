using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services.Events;

namespace media_management_app.Services;

public sealed class AutoTorrentLinkService : IAutoTorrentLinkService
{
    private readonly IDatabaseService _databaseService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IHardlinkService _hardlinkService;
    private readonly ILibraryLinkEventHub _eventHub;
    private readonly ISpecialMappingOrchestrator _specialMappingOrchestrator;
    private readonly IAppLogger _logger;

    public AutoTorrentLinkService(
        IDatabaseService databaseService,
        IQbittorrentClient qbittorrentClient,
        IHardlinkService hardlinkService,
        ILibraryLinkEventHub eventHub,
        ISpecialMappingOrchestrator specialMappingOrchestrator,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _qbittorrentClient = qbittorrentClient;
        _hardlinkService = hardlinkService;
        _eventHub = eventHub;
        _specialMappingOrchestrator = specialMappingOrchestrator;
        _logger = logger;
    }

    public async Task<AutoTorrentLinkResult> LinkEpisodeAsync(long showId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken = default)
    {
        var result = new AutoTorrentLinkResult();
        var show = _databaseService.GetTrackedShow(showId);
        var episode = _databaseService.GetTrackedEpisodes(showId)
            .FirstOrDefault(item => item.SeasonNumber == seasonNumber && item.EpisodeNumber == episodeNumber);
        if (show is null || episode is null)
        {
            AddSkip(result, $"Tracked episode S{seasonNumber:00}E{episodeNumber:00} was not found.");
            return result;
        }

        await LinkEpisodeCoreAsync(show, episode, result, cancellationToken);
        return result;
    }

    public async Task<SeasonPackLinkPreview> PrepareSeasonPackLinkAsync(
        long showId,
        int ownerSeasonNumber,
        IProgress<PackLinkProgressUpdate>? progress = null,
        bool useGeminiForSpecials = true,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        var show = _databaseService.GetTrackedShow(showId);
        var season = _databaseService.GetTrackedSeasons(showId)
            .FirstOrDefault(item => item.SeasonNumber == ownerSeasonNumber);
        if (show is null || season is null)
        {
            throw new InvalidOperationException($"Tracked pack owner season S{ownerSeasonNumber:00} was not found.");
        }

        return await PrepareSeasonPackLinkCoreAsync(
            show,
            season,
            _databaseService.GetTrackedEpisodes(showId),
            progress,
            useGeminiForSpecials,
            bypassCache,
            cancellationToken);
    }

    public async Task<AutoTorrentLinkResult> ApplySeasonPackLinkAsync(
        SeasonPackLinkPreview preview,
        IProgress<PackLinkProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await LinkSeasonPackFromInventoryCoreAsync(
            preview.Show,
            preview.OwnerSeason,
            _databaseService.GetTrackedEpisodes(preview.Show.Id),
            preview.Inventory,
            preview.SpecialMappings,
            progress,
            cancellationToken);

        // Applying a prepared pack is the user's accept (Gemini results always go through review): only now is
        // the special mapping worth caching (PR-15).
        _specialMappingOrchestrator.RememberAccepted(preview.SpecialMappings);
        return result;
    }

    public async Task<AutoTorrentLinkResult> LinkMovieAsync(long movieId, CancellationToken cancellationToken = default)
    {
        var result = new AutoTorrentLinkResult();
        var movie = _databaseService.GetTrackedMovie(movieId);
        if (movie is null)
        {
            AddSkip(result, "Tracked movie was not found.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(movie.TorrentHash))
        {
            AddSkip(result, $"{movie.DisplayTitle}: no mapped qBittorrent hash.");
            return result;
        }

        var torrent = await GetTorrentAsync(movie.TorrentHash, cancellationToken);
        if (torrent is null)
        {
            AddSkip(result, $"{movie.DisplayTitle}: torrent is not present in qBittorrent.");
            return result;
        }

        var videoFiles = await GetCompletedVideoFilesAsync(torrent, cancellationToken);
        if (videoFiles.Count == 0)
        {
            AddSkip(result, $"{movie.DisplayTitle}: no completed video files.");
            return result;
        }

        var selectedFile = videoFiles.OrderByDescending(file => file.Size).First();
        var sourcePath = BuildSourcePath(torrent, selectedFile);
        var sourceItem = CreateMovieSourceItem(movie, torrent, sourcePath);
        LinkSourceItem(sourceItem, result);
        return result;
    }

    public AutoTorrentLinkResult RemoveShowLinks(long showId)
    {
        var show = _databaseService.GetTrackedShow(showId);
        var providerId = show?.TmdbId.ToString();
        return string.IsNullOrWhiteSpace(providerId)
            ? new AutoTorrentLinkResult { SkippedCount = 1, Messages = { "Tracked show was not found." } }
            : RemoveLinks(item => item.MediaKind == MediaKind.TvEpisode &&
                                  string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    public AutoTorrentLinkResult ClearShowLinksForRenumbering(long showId)
    {
        var show = _databaseService.GetTrackedShow(showId);
        var providerId = show?.TmdbId.ToString();
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return new AutoTorrentLinkResult { SkippedCount = 1, Messages = { "Tracked show was not found." } };
        }

        AutoTorrentLinkResult result;
        using (_eventHub.BeginBulkMutation())
        {
            result = RemoveShowLinks(showId);
        }

        if (result.ErrorCount > 0)
        {
            return result;
        }

        // Unlinked rows still carry the old season/episode numbers and would mark the wrong
        // episodes Available under the new numbering (PARSE-26).
        DeleteAcceptedTvSourceItems(providerId, _ => true, result);
        return result;
    }

    public AutoTorrentLinkResult RemoveEpisodeLinks(long showId, int seasonNumber, int episodeNumber)
    {
        var show = _databaseService.GetTrackedShow(showId);
        var providerId = show?.TmdbId.ToString();
        return string.IsNullOrWhiteSpace(providerId)
            ? new AutoTorrentLinkResult { SkippedCount = 1, Messages = { "Tracked show was not found." } }
            : RemoveLinks(item =>
            {
                // A multi-episode file (S01E01-E02) is the link of each episode in its range (audit L3-08).
                return item.MediaKind == MediaKind.TvEpisode &&
                       CoversEpisode(item, seasonNumber, episodeNumber) &&
                       string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase);
            });
    }

    public AutoTorrentLinkResult RemoveSeasonPackLinks(long showId, int ownerSeasonNumber)
    {
        var show = _databaseService.GetTrackedShow(showId);
        var providerId = show?.TmdbId.ToString();
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return new AutoTorrentLinkResult { SkippedCount = 1, Messages = { "Tracked show was not found." } };
        }

        using (_eventHub.BeginBulkMutation())
        {
            var snapshot = _databaseService.GetSourceItems();
            _logger.Info($"Pack unlink SourceItems snapshot: {snapshot.Count} item(s).", LogTarget.All);
            return RemoveLinks(
                item => item.MediaKind == MediaKind.TvEpisode &&
                        string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                        item.AutoTorrentPackOwnerSeasonNumber == ownerSeasonNumber &&
                        (item.AutoTorrentLinkKind == AutoTorrentLinkKind.SeasonPack ||
                         item.IsOrphanPackSpecial),
                preservePackProvenance: true,
                snapshot: snapshot);
        }
    }

    public AutoTorrentLinkResult RemoveMovieLinks(long movieId)
    {
        var movie = _databaseService.GetTrackedMovie(movieId);
        var providerId = movie?.TmdbId.ToString();
        return string.IsNullOrWhiteSpace(providerId)
            ? new AutoTorrentLinkResult { SkippedCount = 1, Messages = { "Tracked movie was not found." } }
            : RemoveLinks(item => item.MediaKind == MediaKind.Movie &&
                                  string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    public AutoTorrentLinkResult RemoveOrphanPackSpecialLink(long sourceItemId)
    {
        var item = _databaseService.GetSourceItems().FirstOrDefault(candidate => candidate.Id == sourceItemId);
        if (item is null || !item.IsOrphanPackSpecial)
        {
            return new AutoTorrentLinkResult
            {
                SkippedCount = 1,
                Messages = { "Orphan pack entry was not found." }
            };
        }

        return RemoveOrphanItem(item);
    }

    public AutoTorrentLinkResult RefreshLinkStatus(long? showId = null, long? movieId = null)
    {
        var result = new AutoTorrentLinkResult();
        var items = _databaseService.GetSourceItems().Where(item =>
            !string.IsNullOrWhiteSpace(item.LinkedPath) &&
            string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase));

        if (showId is not null)
        {
            var showProviderId = _databaseService.GetTrackedShow(showId.Value)?.TmdbId.ToString();
            items = items.Where(item => item.MediaKind == MediaKind.TvEpisode && item.ProviderId == showProviderId);
        }

        if (movieId is not null)
        {
            var movieProviderId = _databaseService.GetTrackedMovie(movieId.Value)?.TmdbId.ToString();
            items = items.Where(item => item.MediaKind == MediaKind.Movie && item.ProviderId == movieProviderId);
        }

        var storage = _hardlinkService.CreateLinkStorageProbe() ?? new LinkStorageProbe();
        foreach (var item in items.ToList())
        {
            if (!storage.IsLinkStorageReachable(item))
            {
                // The library drive or folder is offline: keep the record as it is instead of
                // treating every link on it as deleted (PARSE-27).
                result.SkippedCount++;
                continue;
            }

            item.State = File.Exists(item.LinkedPath) ? ItemState.Linked : ItemState.Parsed;
            if (!File.Exists(item.LinkedPath))
            {
                // The Jellyfin symlink now points at nothing; let the symlink coordinator remove it.
                _eventHub.PublishHardlinkRemoved(item, item.LinkedPath!);
                item.LinkedPath = null;
                item.State = ItemState.Parsed;
                item.Notes = "Linked path no longer exists.";
                if (!ShouldPreservePackProvenance(item))
                {
                    item.AutoTorrentLinkKind = null;
                    item.AutoTorrentTorrentHash = null;
                    item.AutoTorrentPackOwnerSeasonNumber = null;
                }

                result.SkippedCount++;
            }

            _databaseService.UpdateSourceItem(item);
        }

        foreach (var folder in storage.UnreachableFolders)
        {
            _logger.Warning(
                $"Library folder is not reachable, link status left unchanged: {folder}",
                LogTarget.File | LogTarget.Console);
            result.Messages.Add($"Library folder is not reachable, link status left unchanged: {folder}");
        }

        result.Messages.Add("Refreshed link status.");
        return result;
    }

    public AutoTorrentLinkResult ResetEpisodeForRedownload(long showId, int seasonNumber, int episodeNumber)
    {
        var result = new AutoTorrentLinkResult();

        // Remove hardlinks and reset SourceItem link fields (handles the case where file still exists)
        var unlinkResult = RemoveEpisodeLinks(showId, seasonNumber, episodeNumber);
        Merge(result, unlinkResult);
        if (unlinkResult.ErrorCount > 0)
        {
            // Deleting the records now would orphan the library file and its Jellyfin symlink,
            // and the re-download could never be linked ("Output already exists") (PARSE-28).
            _logger.Warning(
                $"Reset aborted for show id={showId} S{seasonNumber:00}E{episodeNumber:00}: {unlinkResult.ErrorCount} unlink error(s).",
                LogTarget.File | LogTarget.Console);
            result.Messages.Add(
                $"Reset aborted: {unlinkResult.ErrorCount} library link(s) could not be removed (see the message above). Fix that and retry.");
            return result;
        }

        // Find the episode record
        var episode = _databaseService.GetTrackedEpisodes(showId)
            .FirstOrDefault(ep => ep.SeasonNumber == seasonNumber && ep.EpisodeNumber == episodeNumber);

        if (episode is null)
        {
            result.Messages.Add($"Episode S{seasonNumber:00}E{episodeNumber:00} not found in database.");
            return result;
        }

        // Delete all SourceItems for this episode so RefreshAvailability sees it as Missing,
        // even when the source file still exists on disk (user is resetting to re-download).
        var show = _databaseService.GetTrackedShow(showId);
        var providerId = show?.TmdbId.ToString();
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            DeleteAcceptedTvSourceItems(
                providerId,
                item => CoversEpisode(item, seasonNumber, episodeNumber),
                result);
        }

        // Clear torrent hash and download state so the episode becomes Missing and re-searchable
        _databaseService.UpdateTrackedEpisodeTorrent(episode.Id, string.Empty, string.Empty, string.Empty, 0);

        // Clear stored search candidate
        _databaseService.ClearTrackedEpisodeSelectedCandidate(episode.Id);

        // Remove any non-terminal cart orders for this episode
        var episodeOrders = _databaseService.GetTorrentCartOrders(MediaKind.TvEpisode, showId)
            .Where(order => order.EpisodeId == episode.Id)
            .ToList();
        foreach (var order in episodeOrders)
        {
            _databaseService.DeleteTorrentCartOrder(order.Id);
        }

        result.Messages.Add($"Reset S{seasonNumber:00}E{episodeNumber:00}: download state cleared.");
        return result;
    }

    public async Task<AutoTorrentLinkResult> ResetSeasonPackForRedownloadAsync(
        long showId,
        int ownerSeasonNumber,
        CancellationToken cancellationToken = default)
    {
        var result = new AutoTorrentLinkResult();
        var show = _databaseService.GetTrackedShow(showId);
        if (show is null)
        {
            AddSkip(result, "Tracked show was not found.");
            return result;
        }

        var ownerSeason = _databaseService.GetTrackedSeasons(showId)
            .FirstOrDefault(season => season.SeasonNumber == ownerSeasonNumber);
        var scope = SeasonPackCleanupMatcher.Resolve(
            ownerSeasonNumber,
            ownerSeason?.SelectedPackCoveredSeasons,
            ownerSeason?.PackTorrentHash,
            ownerSeason?.LastPackLinkTorrentHash);

        HashSet<string> packSourcePaths;
        try
        {
            packSourcePaths = await CollectPackSourcePathsAsync(scope.TorrentHashes, cancellationToken);
        }
        catch (Exception ex)
        {
            packSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _logger.Warning(
                $"Pack cleanup could not list qBittorrent files for show id={showId} S{ownerSeasonNumber:00}: {ex.Message}. Continuing with provenance capture only.",
                LogTarget.File | LogTarget.Console);
        }
        var providerId = show.TmdbId.ToString();
        var sourceItems = _databaseService.GetSourceItems()
            .Where(item =>
                item.MediaKind == MediaKind.TvEpisode &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var capturedIds = SeasonPackCleanupMatcher
            .SelectItems(sourceItems.Select(ToCleanupCandidate), scope, packSourcePaths)
            .Select(item => item.Id)
            .ToHashSet();

        AutoTorrentLinkResult unlinkResult;
        using (_eventHub.BeginBulkMutation())
        {
            _logger.Info(
                $"Pack cleanup unlink SourceItems snapshot: {sourceItems.Count} item(s).",
                LogTarget.All);
            unlinkResult = await Task.Run(
                () => RemoveLinks(
                    item => capturedIds.Contains(item.Id),
                    preservePackProvenance: true,
                    snapshot: sourceItems),
                cancellationToken);
        }

        Merge(result, unlinkResult);
        if (unlinkResult.ErrorCount > 0)
        {
            _logger.Warning(
                $"Pack cleanup aborted for show id={showId} S{ownerSeasonNumber:00}: {unlinkResult.ErrorCount} unlink error(s). Pack metadata was left unchanged.",
                LogTarget.File | LogTarget.Console);
            result.Messages.Add(
                $"Pack cleanup aborted: {unlinkResult.ErrorCount} library link(s) could not be removed. Retry after those files are unlocked.");
            return result;
        }

        var deletedCount = 0;
        foreach (var item in _databaseService.GetSourceItems().Where(item => capturedIds.Contains(item.Id)).ToList())
        {
            _databaseService.DeleteSourceItem(item.Id);
            result.RemovedCount++;
            result.Messages.Add($"Removed source item: {item.FileName}");
            deletedCount++;
        }

        _databaseService.ClearTrackedSeasonSelectedPack(showId, ownerSeasonNumber);

        var packOrders = _databaseService.GetTorrentCartOrders(MediaKind.TvEpisode, showId)
            .Where(order => order.EpisodeId is null && order.SeasonNumber == ownerSeasonNumber)
            .ToList();
        foreach (var order in packOrders)
        {
            _databaseService.DeleteTorrentCartOrder(order.Id);
        }

        var coveredDisplay = scope.CoveredSeasonsDisplay;
        var hashDisplay = scope.TorrentHashes.Count == 0 ? "-" : string.Join(",", scope.TorrentHashes);
        _logger.Info(
            $"Pack cleanup for show id={showId} S{ownerSeasonNumber:00}: covered={coveredDisplay} hash={hashDisplay} deleted {deletedCount} source item(s).",
            LogTarget.File | LogTarget.Console);
        result.Messages.Add($"Reset season S{ownerSeasonNumber:00} pack (covers {coveredDisplay}): download state cleared.");
        return result;
    }

    public AutoTorrentLinkResult ResetMovieForRedownload(long movieId)
    {
        var result = new AutoTorrentLinkResult();

        var unlinkResult = RemoveMovieLinks(movieId);
        Merge(result, unlinkResult);
        if (unlinkResult.ErrorCount > 0)
        {
            _logger.Warning(
                $"Reset aborted for movie id={movieId}: {unlinkResult.ErrorCount} unlink error(s).",
                LogTarget.File | LogTarget.Console);
            result.Messages.Add(
                $"Reset aborted: {unlinkResult.ErrorCount} library link(s) could not be removed (see the message above). Fix that and retry.");
            return result;
        }

        var movie = _databaseService.GetTrackedMovie(movieId);
        if (movie is null)
        {
            result.Messages.Add("Tracked movie was not found.");
            return result;
        }

        var providerId = movie.TmdbId.ToString();
        var movieItems = _databaseService.GetSourceItems()
            .Where(item =>
                item.MediaKind == MediaKind.Movie &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var item in movieItems)
        {
            _databaseService.DeleteSourceItem(item.Id);
            result.Messages.Add($"Removed source item: {item.FileName}");
        }

        _databaseService.UpdateTrackedMovieTorrent(movieId, string.Empty, string.Empty, string.Empty, 0);
        _databaseService.ClearTrackedMovieSelectedCandidate(movieId);

        var movieOrders = _databaseService.GetTorrentCartOrders(MediaKind.Movie, movieId).ToList();
        foreach (var order in movieOrders)
        {
            _databaseService.DeleteTorrentCartOrder(order.Id);
        }

        result.Messages.Add($"Reset movie '{movie.DisplayTitle}': download state cleared.");
        return result;
    }

    private async Task LinkEpisodeCoreAsync(TrackedShow show, TrackedEpisode episode, AutoTorrentLinkResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(episode.TorrentHash))
        {
            AddSkip(result, $"{show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}: no mapped qBittorrent hash.");
            return;
        }

        var torrent = await GetTorrentAsync(episode.TorrentHash, cancellationToken);
        if (torrent is null)
        {
            AddSkip(result, $"{show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}: torrent is not present in qBittorrent.");
            return;
        }

        var videoFiles = await GetCompletedVideoFilesAsync(torrent, cancellationToken);
        var selectedFile = SelectEpisodeFile(videoFiles, episode);
        if (selectedFile is null)
        {
            AddSkip(result, $"{show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}: no unambiguous completed video file.");
            return;
        }

        if (NamesAnotherEpisode(selectedFile, episode, out var namedEpisode, out var fileRange))
        {
            // A single-file torrent is taken as-is; refuse it when its name says it is another
            // episode (for example a torrent adopted under the wrong episode) (PARSE-25).
            AddSkip(
                result,
                $"{show.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}: file '{Path.GetFileName(selectedFile.Name)}' is {namedEpisode}, not linked.");
            return;
        }

        var episodeItem = CreateEpisodeSourceItem(
            show,
            episode,
            torrent,
            BuildSourcePath(torrent, selectedFile),
            AutoTorrentLinkKind.Episode,
            packOwnerSeasonNumber: null);
        if (fileRange is { } range && RangeIsInTmdb(show.Id, episode.SeasonNumber, range.First, range.Last))
        {
            // One file holding several episodes is linked once as "S01E01-E02", whichever of its
            // episodes the torrent was assigned to (audit L1-M2).
            episodeItem.EpisodeNumber = range.First;
            episodeItem.LastEpisodeNumber = range.Last;
        }

        LinkSourceItem(episodeItem, result);
    }

    private bool RangeIsInTmdb(long showId, int seasonNumber, int firstEpisode, int lastEpisode)
    {
        var episodes = _databaseService.GetTrackedEpisodes(showId)
            .Where(item => item.SeasonNumber == seasonNumber)
            .Select(item => item.EpisodeNumber)
            .ToHashSet();
        return Enumerable.Range(firstEpisode, lastEpisode - firstEpisode + 1).All(episodes.Contains);
    }

    private async Task<SeasonPackLinkPreview> PrepareSeasonPackLinkCoreAsync(
        TrackedShow show,
        TrackedSeason ownerSeason,
        IReadOnlyList<TrackedEpisode> episodes,
        IProgress<PackLinkProgressUpdate>? progress,
        bool useGeminiForSpecials,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerSeason.PackTorrentHash))
        {
            throw new InvalidOperationException($"{show.DisplayTitle} S{ownerSeason.SeasonNumber:00}: no mapped pack torrent hash.");
        }

        var torrent = await GetTorrentAsync(ownerSeason.PackTorrentHash, cancellationToken);
        if (torrent is null)
        {
            throw new InvalidOperationException($"{show.DisplayTitle} S{ownerSeason.SeasonNumber:00}: pack torrent is not present in qBittorrent.");
        }

        var coveredSeasons = ParseCoveredSeasons(ownerSeason.SelectedPackCoveredSeasons).ToHashSet();
        if (coveredSeasons.Count == 0)
        {
            coveredSeasons.Add(ownerSeason.SeasonNumber);
        }

        var seasons = _databaseService.GetTrackedSeasons(show.Id);
        var files = await GetCompletedVideoFilesAsync(torrent, cancellationToken);
        var fileTuples = files.Select(file => (file.Name, Path.GetFileName(file.Name))).ToList();
        var fileSizes = BuildFileSizes(files);
        var tree = PackFolderTreeAnalyzer.Analyze(fileTuples.Select(file => file.Name).ToList());

        IReadOnlyDictionary<int, SeasonRegularEpisodeStats> regularEpisodesBySeason =
            new Dictionary<int, SeasonRegularEpisodeStats>();

        if (useGeminiForSpecials)
        {
            PackLinkProgressReporter.Report(
                progress,
                PackLinkProgressStep.MapRegularEpisodes,
                PackLinkProgressStatus.Active,
                "Mapping regular episodes (rules)...");

            var regularOnlyMappings = new SpecialMappingResult();
            var regularInventory = PackTorrentInventoryAnalyzer.Analyze(
                fileTuples,
                episodes,
                seasons,
                coveredSeasons,
                PackAnalyzeMode.Link,
                regularOnlyMappings,
                ownerSeasonNumber: ownerSeason.SeasonNumber,
                fileSizes: fileSizes);

            regularEpisodesBySeason = PackLinkRegularEpisodeSummary.Build(
                regularInventory,
                episodes,
                coveredSeasons);

            PackLinkProgressReporter.Report(
                progress,
                PackLinkProgressStep.MapRegularEpisodes,
                PackLinkProgressStatus.Done,
                PackLinkRegularEpisodeSummary.FormatSummary(regularEpisodesBySeason));
        }

        var resolvedSpecialMappings = await ResolveSpecialMappingsAsync(
            show,
            ownerSeason,
            fileTuples,
            tree,
            episodes,
            progress,
            useGeminiForSpecials,
            bypassCache,
            cancellationToken);

        var inventory = PackTorrentInventoryAnalyzer.Analyze(
            fileTuples,
            episodes,
            seasons,
            coveredSeasons,
            PackAnalyzeMode.Link,
            resolvedSpecialMappings,
            ownerSeasonNumber: ownerSeason.SeasonNumber,
            fileSizes: fileSizes);

        return new SeasonPackLinkPreview
        {
            Show = show,
            OwnerSeason = ownerSeason,
            Inventory = inventory,
            SpecialMappings = resolvedSpecialMappings,
            RequiresReview = SpecialMappingReviewPolicy.RequiresReview(resolvedSpecialMappings),
            UsedGeminiForSpecials = useGeminiForSpecials,
            RegularEpisodeCount = inventory.Files.Count(file => file.Classification == PackFileClassification.RegularEpisode),
            MatchedSpecialCount = inventory.Files.Count(file => file.Classification == PackFileClassification.MatchedSpecial),
            OrphanExtraCount = inventory.Files.Count(file => file.Classification == PackFileClassification.UnmatchedExtra),
            RegularEpisodesBySeason = regularEpisodesBySeason
        };
    }

    /// <summary>
    /// Runs the special/OVA step. Any failure there (Gemini, response parsing, a bug in the payload code) falls
    /// back to the offline rule mappings and asks for review, so the regular episodes of the pack still link.
    /// Only a user cancellation stops the whole prepare.
    /// </summary>
    private async Task<SpecialMappingResult> ResolveSpecialMappingsAsync(
        TrackedShow show,
        TrackedSeason ownerSeason,
        IReadOnlyList<(string RelativePath, string FileName)> fileTuples,
        PackFolderTreeAnalysis tree,
        IReadOnlyList<TrackedEpisode> episodes,
        IProgress<PackLinkProgressUpdate>? progress,
        bool useGeminiForSpecials,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _specialMappingOrchestrator.ResolveAsync(
                       fileTuples,
                       tree,
                       episodes,
                       show.DisplayTitle,
                       show.TmdbId,
                       ownerSeason.PackTorrentHash!,
                       progress,
                       useGeminiForSpecials,
                       bypassCache,
                       cancellationToken)
                   ?? throw new InvalidOperationException("Special mapping returned no result.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var reason = $"Special/OVA mapping failed: {ex.Message}";
            _logger.Warning(
                $"Pack link '{show.DisplayTitle}' S{ownerSeason.SeasonNumber:00}: {reason} Regular episodes are not affected.",
                LogTarget.All);
            PackLinkProgressReporter.Report(
                progress,
                PackLinkProgressStep.BuildingPayload,
                PackLinkProgressStatus.Done,
                "Special/OVA mapping failed. Using rule mappings; review required.",
                ex.Message);

            try
            {
                return SpecialMappingOrchestrator.ResolveOffline(
                    fileTuples,
                    tree,
                    episodes,
                    show.DisplayTitle,
                    show.TmdbId,
                    reason);
            }
            catch (Exception offlineError)
            {
                _logger.Error(
                    $"Pack link '{show.DisplayTitle}' S{ownerSeason.SeasonNumber:00}: rule mappings for specials also failed.",
                    offlineError,
                    LogTarget.All);
                return new SpecialMappingResult().WithReview(
                    [$"{reason} Rule mappings also failed ({offlineError.Message}); special/OVA files are left out."],
                    reason);
            }
        }
    }

    private async Task<AutoTorrentLinkResult> LinkSeasonPackFromInventoryCoreAsync(
        TrackedShow show,
        TrackedSeason ownerSeason,
        IReadOnlyList<TrackedEpisode> episodes,
        PackTorrentInventory inventory,
        SpecialMappingResult? resolvedSpecialMappings,
        IProgress<PackLinkProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var result = new AutoTorrentLinkResult();
        if (string.IsNullOrWhiteSpace(ownerSeason.PackTorrentHash))
        {
            AddSkip(result, $"{show.DisplayTitle} S{ownerSeason.SeasonNumber:00}: no mapped pack torrent hash.");
            return result;
        }

        var torrent = await GetTorrentAsync(ownerSeason.PackTorrentHash, cancellationToken);
        if (torrent is null)
        {
            AddSkip(result, $"{show.DisplayTitle} S{ownerSeason.SeasonNumber:00}: pack torrent is not present in qBittorrent.");
            return result;
        }

        var episodesByKey = episodes.ToDictionary(episode => (episode.SeasonNumber, episode.EpisodeNumber));
        var linkableEntries = inventory.Files
            .Where(file => file.Classification is PackFileClassification.RegularEpisode
                or PackFileClassification.MatchedSpecial
                or PackFileClassification.UnmatchedExtra)
            .ToList();

        var regularLinked = 0;
        var specialsMatched = 0;
        var orphansLinked = 0;

        PackLinkProgressReporter.Report(
            progress,
            PackLinkProgressStep.ApplyingLinks,
            PackLinkProgressStatus.Active,
            "Creating library links...");

        using (_eventHub.BeginBulkMutation())
        {
            var snapshot = _databaseService.GetSourceItems();
            var index = SourceItemPathIndex.From(snapshot);
            _logger.Info($"Pack link SourceItems snapshot: {snapshot.Count} item(s).", LogTarget.All);

            await Task.Run(() =>
            {
                var releasedAnyLink = false;
                var blockedByAnotherFile = new List<(Func<SourceItem> Create, string SkipMessage)>();
                void LinkPackItem(Func<SourceItem> create)
                {
                    var attempt = LinkSourceItem(create(), result, index);
                    releasedAnyLink |= attempt.ReleasedOldLink;
                    if (attempt.SkippedAsDuplicate)
                    {
                        blockedByAnotherFile.Add((create, attempt.DuplicateSkipMessage!));
                    }
                }

                foreach (var entry in inventory.Files.Where(file => file.Classification == PackFileClassification.Movie))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddSkip(result, $"{show.DisplayTitle}: skipped movie pack file '{entry.FileName}'.");
                }

                foreach (var entry in inventory.Files.Where(file => file.Classification == PackFileClassification.Skipped))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddSkip(result, $"{show.DisplayTitle}: skipped unmatched pack file '{entry.FileName}'.");
                }

                foreach (var entry in linkableEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourcePath = BuildSourcePath(torrent, new TorrentContentFile
                    {
                        Name = entry.RelativePath,
                        Size = 0,
                        Progress = 1
                    });

                    switch (entry.Classification)
                    {
                        case PackFileClassification.UnmatchedExtra:
                            if (LinkOrphanPackItem(show, torrent, sourcePath, ownerSeason.SeasonNumber, result, index))
                            {
                                orphansLinked++;
                            }

                            break;
                        case PackFileClassification.MatchedSpecial when entry.MatchedSeasonNumber is not null &&
                                                                        entry.MatchedEpisodeNumber is not null &&
                                                                        episodesByKey.TryGetValue(
                                                                            (entry.MatchedSeasonNumber.Value, entry.MatchedEpisodeNumber.Value),
                                                                            out var specialEpisode):
                            var special = specialEpisode;
                            var specialPath = sourcePath;
                            LinkPackItem(() => CreateEpisodeSourceItem(
                                show,
                                special,
                                torrent,
                                specialPath,
                                AutoTorrentLinkKind.SeasonPack,
                                ownerSeason.SeasonNumber));
                            specialsMatched++;
                            break;
                        case PackFileClassification.RegularEpisode when entry.MatchedSeasonNumber is not null &&
                                                                          entry.MatchedEpisodeNumber is not null &&
                                                                          episodesByKey.TryGetValue(
                                                                              (entry.MatchedSeasonNumber.Value, entry.MatchedEpisodeNumber.Value),
                                                                              out var episode):
                            var regular = episode;
                            var regularPath = sourcePath;
                            var lastEpisode = entry.MatchedLastEpisodeNumber;
                            LinkPackItem(() =>
                            {
                                var regularItem = CreateEpisodeSourceItem(
                                    show,
                                    regular,
                                    torrent,
                                    regularPath,
                                    AutoTorrentLinkKind.SeasonPack,
                                    ownerSeason.SeasonNumber);
                                regularItem.LastEpisodeNumber = lastEpisode;
                                return regularItem;
                            });
                            regularLinked++;
                            break;
                    }
                }

                // Two files that swap episodes on re-link: the first was skipped because the second still held
                // its new episode, and the second then moved away. Retry those once the pass is done (audit L3-06).
                if (releasedAnyLink)
                {
                    foreach (var (create, skipMessage) in blockedByAnotherFile)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var linkedBefore = result.LinkedCount;
                        var retry = LinkSourceItem(create(), result, index);
                        if (result.LinkedCount > linkedBefore)
                        {
                            result.SkippedCount--;
                            result.Messages.Remove(skipMessage);
                        }
                        else if (retry.SkippedAsDuplicate)
                        {
                            // Still provided by another file: keep the first message only.
                            result.SkippedCount--;
                            result.Messages.RemoveAt(result.Messages.LastIndexOf(retry.DuplicateSkipMessage!));
                        }
                    }
                }
            }, cancellationToken);
        }

        result.Messages.Add(inventory.BuildSummaryText());
        if (regularLinked > 0 || specialsMatched > 0 || orphansLinked > 0)
        {
            result.Messages.Add(
                $"Pack summary: {regularLinked} episodes, {specialsMatched} Extras/Specials/OVAs (TMDB matched), {orphansLinked} orphan Extras/Specials/OVAs.");
        }

        if (inventory.Warnings.Count > 0)
        {
            result.Messages.AddRange(inventory.Warnings);
        }

        _logger.Info(
            $"Pack inventory link for '{show.DisplayTitle}' S{ownerSeason.SeasonNumber:00}: {regularLinked} episodes, {specialsMatched} specials, {orphansLinked} unmatched extras. UsedGemini={resolvedSpecialMappings?.UsedGemini == true}.",
            LogTarget.All);

        var linkSummary =
            $"{regularLinked} episodes, {specialsMatched} specials, {orphansLinked} orphan extras.";
        PackLinkProgressReporter.Report(
            progress,
            PackLinkProgressStep.ApplyingLinks,
            PackLinkProgressStatus.Done,
            linkSummary);

        return result;
    }

    private bool LinkOrphanPackItem(
        TrackedShow show,
        AddedTorrentResult torrent,
        string sourcePath,
        int packOwnerSeasonNumber,
        AutoTorrentLinkResult result,
        SourceItemPathIndex? index = null)
    {
        var beforeLinked = result.LinkedCount;
        LinkSourceItem(
            CreateOrphanPackSourceItem(show, torrent, sourcePath, packOwnerSeasonNumber),
            result,
            index);
        return result.LinkedCount > beforeLinked;
    }

    private static SourceItem CreateOrphanPackSourceItem(
        TrackedShow show,
        AddedTorrentResult torrent,
        string sourcePath,
        int packOwnerSeasonNumber)
    {
        return new SourceItem
        {
            SourceRootFolder = torrent.SavePath,
            ParentFolder = Path.GetDirectoryName(sourcePath) ?? torrent.SavePath,
            FilePath = sourcePath,
            FileName = Path.GetFileName(sourcePath),
            ScanText = Path.GetFileNameWithoutExtension(sourcePath),
            MediaKind = MediaKind.TvEpisode,
            ParserPattern = ParserPattern.StandardTv,
            ShowTitle = show.Title,
            SeasonNumber = AppConstants.SpecialsSeasonNumber,
            MatchedTitle = show.Title,
            MatchedYear = show.FirstAirYear,
            Provider = "tmdb",
            ProviderId = show.TmdbId.ToString(),
            MatchConfidence = 100,
            MatchReason = "Orphan pack extra/special/OVA from season pack link.",
            RequiresManualReview = false,
            MatchAccepted = true,
            State = ItemState.Parsed,
            AutoTorrentLinkKind = AutoTorrentLinkKind.SeasonPack,
            AutoTorrentTorrentHash = torrent.Hash,
            AutoTorrentPackOwnerSeasonNumber = packOwnerSeasonNumber,
            IsOrphanPackSpecial = true,
            LastSeenUtc = DateTime.UtcNow
        };
    }

    /// <summary>What <see cref="LinkSourceItem"/> did; a pack re-link uses it to retry files that a swap blocked.</summary>
    private readonly record struct LinkAttempt(bool ReleasedOldLink, string? DuplicateSkipMessage)
    {
        public bool SkippedAsDuplicate => DuplicateSkipMessage is not null;
    }

    private LinkAttempt LinkSourceItem(
        SourceItem sourceItem,
        AutoTorrentLinkResult result,
        SourceItemPathIndex? index = null)
    {
        TryMigrateLegacyOrphanPath(sourceItem, result, index);

        var releasedChangedLink = false;
        if (TryGetExistingLinkedItem(sourceItem, index, out var existingItem) && existingItem is not null)
        {
            if (HasSameLinkTarget(existingItem, sourceItem))
            {
                existingItem.AutoTorrentLinkKind = sourceItem.AutoTorrentLinkKind;
                existingItem.AutoTorrentTorrentHash = sourceItem.AutoTorrentTorrentHash;
                existingItem.AutoTorrentPackOwnerSeasonNumber = sourceItem.AutoTorrentPackOwnerSeasonNumber;
                existingItem.LastSeenUtc = DateTime.UtcNow;
                _databaseService.UpdateSourceItem(existingItem);
                index?.Replace(existingItem);
                AddSkip(result, $"{sourceItem.DisplayTitle}: already linked.");
                return default;
            }

            // The same file now maps to another episode, or flips between special and orphan
            // extra: replace the old link instead of keeping it (PARSE-12, PARSE-24).
            if (!TryReleaseChangedLink(existingItem, sourceItem, result, index))
            {
                return default;
            }

            releasedChangedLink = true;
        }

        if (TryGetOtherLinkedItemForEpisode(sourceItem, index, out var otherItem, out var partialOverlap) && otherItem is not null)
        {
            // Another file already provides this episode; a second link (often with another
            // extension) would duplicate it in Jellyfin (PARSE-32).
            if (releasedChangedLink)
            {
                PersistUnlinkedMapping(sourceItem, index, $"Episode already linked from {otherItem.FileName}.");
            }

            var skipMessage = $"{sourceItem.DisplayTitle}: already linked from {otherItem.FileName}; skipped {sourceItem.FileName}.";
            AddSkip(result, skipMessage);
            return new LinkAttempt(releasedChangedLink, skipMessage);
        }

        if (partialOverlap is not null)
        {
            result.Messages.Add(
                $"{sourceItem.FileName}: some of its episodes are already linked from {partialOverlap.FileName}; linked for the rest.");
        }

        if (_hardlinkService.CreateHardLink(sourceItem, out var linkedPath, out var errorMessage))
        {
            sourceItem.State = ItemState.Linked;
            sourceItem.LinkedPath = linkedPath;
            sourceItem.Notes = null;
            _databaseService.UpdateSourceItem(sourceItem);
            index?.Replace(sourceItem);
            _eventHub.PublishHardlinkCreated(sourceItem, linkedPath!);
            result.LinkedCount++;
            result.Messages.Add($"Linked {sourceItem.FileName}");
            return new LinkAttempt(releasedChangedLink, null);
        }

        if (releasedChangedLink)
        {
            PersistUnlinkedMapping(sourceItem, index, errorMessage);
        }

        result.ErrorCount++;
        result.Messages.Add($"{sourceItem.FileName}: {errorMessage}");
        return new LinkAttempt(releasedChangedLink, null);
    }

    /// <summary>
    /// True when an existing link already gives the new item the same library identity:
    /// same media, same episode key and same special/orphan role. A missing provider id on a
    /// legacy record is treated as unknown rather than as a change.
    /// </summary>
    private static bool HasSameLinkTarget(SourceItem existing, SourceItem candidate)
    {
        if (existing.MediaKind != candidate.MediaKind ||
            existing.IsOrphanPackSpecial != candidate.IsOrphanPackSpecial ||
            GetOutputEpisodeKey(existing) != GetOutputEpisodeKey(candidate) ||
            !GetOutputEpisodeKeys(existing).SequenceEqual(GetOutputEpisodeKeys(candidate)))
        {
            // A file linked as S01E01 before multi-episode support now resolves to S01E01-E02: re-link it
            // under the range name (audit L1-M2, L3-08).
            return false;
        }

        return string.IsNullOrWhiteSpace(existing.ProviderId) ||
               string.IsNullOrWhiteSpace(candidate.ProviderId) ||
               string.Equals(existing.ProviderId, candidate.ProviderId, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryReleaseChangedLink(
        SourceItem existingItem,
        SourceItem sourceItem,
        AutoTorrentLinkResult result,
        SourceItemPathIndex? index)
    {
        var change = $"{DescribeLinkTarget(existingItem)} -> {DescribeLinkTarget(sourceItem)}";
        if (!_hardlinkService.RemoveHardLink(existingItem, out _, out var removeError))
        {
            result.ErrorCount++;
            result.Messages.Add(
                $"{sourceItem.FileName}: mapping changed ({change}) but the old library link could not be removed: {removeError}");
            return false;
        }

        existingItem.LinkedPath = null;
        existingItem.State = ItemState.Parsed;
        _databaseService.UpdateSourceItem(existingItem);
        index?.Replace(existingItem);
        result.Messages.Add($"{sourceItem.FileName}: mapping changed ({change}); old library link removed.");
        _logger.Info($"Link mapping changed for {sourceItem.FilePath}: {change}.", LogTarget.File | LogTarget.Console);
        return true;
    }

    /// <summary>Records the new mapping of a file whose old link was removed but whose new link was not created.</summary>
    private void PersistUnlinkedMapping(SourceItem sourceItem, SourceItemPathIndex? index, string? reason)
    {
        sourceItem.State = ItemState.Parsed;
        sourceItem.LinkedPath = null;
        sourceItem.Notes = reason;
        _databaseService.UpdateSourceItem(sourceItem);
        index?.Replace(sourceItem);
    }

    /// <summary>
    /// True when other linked files already provide every episode of <paramref name="sourceItem"/> (its whole
    /// range for a multi-episode file), so a new link would only duplicate them in Jellyfin (PARSE-32).
    /// <paramref name="partialOverlap"/> names a file that provides only some of them: the new file is still
    /// linked, because it is the only source of the rest (audit L3-08).
    /// </summary>
    private bool TryGetOtherLinkedItemForEpisode(
        SourceItem sourceItem,
        SourceItemPathIndex? index,
        out SourceItem? otherItem,
        out SourceItem? partialOverlap)
    {
        otherItem = null;
        partialOverlap = null;
        var keys = GetOutputEpisodeKeys(sourceItem).ToHashSet();
        if (sourceItem.MediaKind != MediaKind.TvEpisode ||
            sourceItem.IsOrphanPackSpecial ||
            keys.Count == 0 ||
            string.IsNullOrWhiteSpace(sourceItem.ProviderId))
        {
            return false;
        }

        IEnumerable<SourceItem> candidates = index is not null ? index.All() : _databaseService.GetSourceItems();
        var overlapping = candidates
            .Where(item =>
                item.MediaKind == MediaKind.TvEpisode &&
                !item.IsOrphanPackSpecial &&
                !string.Equals(item.FilePath, sourceItem.FilePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, sourceItem.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                GetOutputEpisodeKeys(item).Any(keys.Contains) &&
                !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                File.Exists(item.LinkedPath))
            .ToList();
        if (overlapping.Count == 0)
        {
            return false;
        }

        var covered = overlapping.SelectMany(GetOutputEpisodeKeys).ToHashSet();
        if (keys.All(covered.Contains))
        {
            otherItem = overlapping[0];
            return true;
        }

        partialOverlap = overlapping[0];
        return false;
    }

    private static bool CoversEpisode(SourceItem item, int seasonNumber, int episodeNumber) =>
        GetOutputEpisodeKeys(item).Contains((seasonNumber, episodeNumber));

    // The episodes a link provides: one key, or the whole range of a multi-episode file (PARSE-06).
    private static IEnumerable<(int SeasonNumber, int EpisodeNumber)> GetOutputEpisodeKeys(SourceItem item)
    {
        if (GetOutputEpisodeKey(item) is not { } key)
        {
            yield break;
        }

        yield return key;
        if (item.MappedEpisodeNumber is null && item.LastEpisodeNumber is { } last)
        {
            for (var episode = key.EpisodeNumber + 1; episode <= last; episode++)
            {
                yield return (key.SeasonNumber, episode);
            }
        }
    }

    private static string DescribeLinkTarget(SourceItem item)
    {
        if (item.IsOrphanPackSpecial)
        {
            return "orphan extra";
        }

        var key = GetOutputEpisodeKey(item);
        return key is null
            ? item.MediaKind.ToString()
            : $"S{key.Value.SeasonNumber:00}E{key.Value.EpisodeNumber:00}";
    }

    private void TryMigrateLegacyOrphanPath(
        SourceItem sourceItem,
        AutoTorrentLinkResult result,
        SourceItemPathIndex? index)
    {
        if (!sourceItem.IsOrphanPackSpecial)
        {
            return;
        }

        var existingOrphan = index is not null
            ? index.FindLegacyOrphan(sourceItem.FilePath)
            : _databaseService.GetSourceItems().FirstOrDefault(item =>
                string.Equals(item.FilePath, sourceItem.FilePath, StringComparison.OrdinalIgnoreCase) &&
                item.IsOrphanPackSpecial &&
                !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                File.Exists(item.LinkedPath) &&
                IsLegacyOrphanSeasonPath(item.LinkedPath));

        if (existingOrphan is null)
        {
            return;
        }

        if (_hardlinkService.RemoveHardLink(existingOrphan, out _, out var errorMessage))
        {
            existingOrphan.LinkedPath = null;
            existingOrphan.State = ItemState.Parsed;
            _databaseService.UpdateSourceItem(existingOrphan);
            index?.Replace(existingOrphan);
            result.Messages.Add($"Moved orphan {existingOrphan.FileName} to the Jellyfin extras folder.");
            return;
        }

        _logger.Warning($"Could not migrate legacy orphan path for {existingOrphan.FileName}: {errorMessage}", LogTarget.All);
    }

    private static bool IsLegacyOrphanSeasonPath(string linkedPath)
    {
        var normalized = linkedPath.Replace('\\', '/');
        return normalized.Contains("/Season 00/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/Season 0/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"/{AppConstants.LegacyOrphanExtrasFolderName}/", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetExistingLinkedItem(
        SourceItem sourceItem,
        SourceItemPathIndex? index,
        out SourceItem? existingItem)
    {
        existingItem = index is not null
            ? index.FindExistingLinked(sourceItem.FilePath)
            : _databaseService.GetSourceItems().FirstOrDefault(item =>
                string.Equals(item.FilePath, sourceItem.FilePath, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                File.Exists(item.LinkedPath));
        return existingItem is not null;
    }

    private AutoTorrentLinkResult RemoveLinks(
        Func<SourceItem, bool> predicate,
        bool preservePackProvenance = false,
        IReadOnlyList<SourceItem>? snapshot = null)
    {
        var result = new AutoTorrentLinkResult();
        var source = snapshot ?? _databaseService.GetSourceItems();
        var matched = source.Where(predicate).ToList();
        // A legacy record and an import of the generated library file can share one LinkedPath.
        // Unlinking only one of them left the other "Linked", so the symlink came back on the next
        // sync. Records that share a LinkedPath with a matched record are handled with it.
        // Only records of the same media count: an old import matched to another show or a movie must not take
        // this show's library file down with it (audit L3-07).
        var matchedIds = matched.Select(item => item.Id).ToHashSet();
        var matchedByLinkedPath = matched
            .Where(item => !string.IsNullOrWhiteSpace(item.LinkedPath))
            .GroupBy(item => Path.GetFullPath(item.LinkedPath!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var coOwners = source
            .Where(item => !matchedIds.Contains(item.Id) &&
                           !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                           matchedByLinkedPath.TryGetValue(Path.GetFullPath(item.LinkedPath), out var owners) &&
                           owners.Any(owner => IsSameMedia(owner, item)))
            .ToList();
        foreach (var item in coOwners.Concat(matched))
        {
            if (string.IsNullOrWhiteSpace(item.LinkedPath))
            {
                continue;
            }

            if (preservePackProvenance &&
                item.IsExternalImport &&
                string.Equals(Path.GetFullPath(item.LinkedPath), Path.GetFullPath(item.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                // An import of the generated library file (PARSE-30 duplicate): the file goes with the pack's
                // own record, so only this record is dropped. RemoveHardLink would refuse it as "source path"
                // and abort the pack reset (audit L3-07).
                _databaseService.DeleteSourceItem(item.Id);
                result.RemovedCount++;
                result.Messages.Add($"Removed duplicate import record for {item.FileName}.");
                continue;
            }

            if (!preservePackProvenance && item.IsExternalImport)
            {
                var linkedPathMatchesSource = string.Equals(
                    Path.GetFullPath(item.LinkedPath),
                    Path.GetFullPath(item.FilePath),
                    StringComparison.OrdinalIgnoreCase);
                if (linkedPathMatchesSource)
                {
                    _eventHub.PublishHardlinkRemoved(item, item.LinkedPath);
                }
                else if (!_hardlinkService.RemoveHardLink(item, out _, out var errorMessage))
                {
                    result.ErrorCount++;
                    result.Messages.Add($"{item.FileName}: {errorMessage}");
                    continue;
                }

                _databaseService.DeleteSourceItem(item.Id);
                result.RemovedCount++;
                result.Messages.Add($"Removed imported media links and library record for {item.FileName}. Original file was kept.");
                continue;
            }

            if (!preservePackProvenance && item.IsOrphanPackSpecial)
            {
                if (_hardlinkService.RemoveHardLink(item, out _, out var errorMessage))
                {
                    _databaseService.DeleteSourceItem(item.Id);
                    result.RemovedCount++;
                    result.Messages.Add($"Removed orphan pack entry {item.FileName}.");
                    continue;
                }

                result.ErrorCount++;
                result.Messages.Add($"{item.FileName}: {errorMessage}");
                continue;
            }

            if (_hardlinkService.RemoveHardLink(item, out _, out var removeErrorMessage))
            {
                item.LinkedPath = null;
                item.State = ItemState.Parsed;
                item.Notes = null;
                if (!preservePackProvenance)
                {
                    item.AutoTorrentLinkKind = null;
                    item.AutoTorrentTorrentHash = null;
                    item.AutoTorrentPackOwnerSeasonNumber = null;
                }

                _databaseService.UpdateSourceItem(item);
                result.RemovedCount++;
                continue;
            }

            result.ErrorCount++;
            result.Messages.Add($"{item.FileName}: {removeErrorMessage}");
        }

        return result;
    }

    private static bool IsSameMedia(SourceItem first, SourceItem second) =>
        first.MediaKind == second.MediaKind &&
        (string.IsNullOrWhiteSpace(first.ProviderId) ||
         string.IsNullOrWhiteSpace(second.ProviderId) ||
         string.Equals(first.ProviderId, second.ProviderId, StringComparison.OrdinalIgnoreCase));

    private AutoTorrentLinkResult RemoveOrphanItem(SourceItem item)
    {
        var result = new AutoTorrentLinkResult();
        if (string.IsNullOrWhiteSpace(item.LinkedPath))
        {
            _databaseService.DeleteSourceItem(item.Id);
            result.RemovedCount++;
            result.Messages.Add($"Removed orphan pack entry {item.FileName}.");
            return result;
        }

        if (_hardlinkService.RemoveHardLink(item, out _, out var errorMessage))
        {
            _databaseService.DeleteSourceItem(item.Id);
            result.RemovedCount++;
            result.Messages.Add($"Removed orphan pack entry {item.FileName}.");
            return result;
        }

        result.ErrorCount++;
        result.Messages.Add($"{item.FileName}: {errorMessage}");
        return result;
    }

    private int DeleteAcceptedTvSourceItems(
        string providerId,
        Func<SourceItem, bool> match,
        AutoTorrentLinkResult result)
    {
        var items = _databaseService.GetSourceItems()
            .Where(item =>
                item.MediaKind == MediaKind.TvEpisode &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                match(item))
            .ToList();

        foreach (var item in items)
        {
            _databaseService.DeleteSourceItem(item.Id);
            result.RemovedCount++;
            result.Messages.Add($"Removed source item: {item.FileName}");
        }

        return items.Count;
    }

    private static (int SeasonNumber, int EpisodeNumber)? GetOutputEpisodeKey(SourceItem item)
    {
        if (item.MappedSeasonNumber is not null && item.MappedEpisodeNumber is not null)
        {
            return (item.MappedSeasonNumber.Value, item.MappedEpisodeNumber.Value);
        }

        if (item.SeasonNumber is not null && item.EpisodeNumber is not null)
        {
            return (item.SeasonNumber.Value, item.EpisodeNumber.Value);
        }

        return null;
    }

    private async Task<AddedTorrentResult?> GetTorrentAsync(string hash, CancellationToken cancellationToken)
    {
        return (await _qbittorrentClient.GetTorrentsAsync(cancellationToken))
            .FirstOrDefault(torrent => string.Equals(torrent.Hash, hash, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<TorrentContentFile>> GetCompletedVideoFilesAsync(AddedTorrentResult torrent, CancellationToken cancellationToken)
    {
        return (await _qbittorrentClient.GetTorrentFilesAsync(torrent.Hash, cancellationToken))
            .Where(file => file.IsVideoFile && file.IsComplete && !TorrentFileRules.IsSampleOrProof(file.Name))
            .ToList();
    }

    private static IReadOnlyDictionary<string, long> BuildFileSizes(IReadOnlyList<TorrentContentFile> files)
    {
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            sizes[file.Name] = file.Size;
        }

        return sizes;
    }

    private async Task<HashSet<string>> CollectPackSourcePathsAsync(
        IReadOnlyList<string> torrentHashes,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hash in torrentHashes)
        {
            var torrent = await GetTorrentAsync(hash, cancellationToken);
            if (torrent is null)
            {
                continue;
            }

            var files = await _qbittorrentClient.GetTorrentFilesAsync(torrent.Hash, cancellationToken);
            foreach (var file in files.Where(item => item.IsVideoFile))
            {
                paths.Add(SeasonPackCleanupMatcher.NormalizePath(BuildSourcePath(torrent, file)));
            }
        }

        return paths;
    }

    private static SeasonPackCleanupCandidate ToCleanupCandidate(SourceItem item) =>
        new()
        {
            Id = item.Id,
            IsExternalImport = item.IsExternalImport,
            IsOrphanPackSpecial = item.IsOrphanPackSpecial,
            PackOwnerSeasonNumber = item.AutoTorrentPackOwnerSeasonNumber,
            TorrentHash = item.AutoTorrentTorrentHash,
            IsSeasonPackLink = item.AutoTorrentLinkKind == AutoTorrentLinkKind.SeasonPack,
            FilePath = item.FilePath
        };

    private static bool ShouldPreservePackProvenance(SourceItem item) =>
        item.AutoTorrentLinkKind == AutoTorrentLinkKind.SeasonPack ||
        item.AutoTorrentPackOwnerSeasonNumber is not null ||
        item.IsOrphanPackSpecial;

    private static TorrentContentFile? SelectEpisodeFile(IReadOnlyList<TorrentContentFile> files, TrackedEpisode episode)
    {
        if (files.Count == 1)
        {
            return files[0];
        }

        return TorrentFileRules.SelectEpisodeFile(files, episode.SeasonNumber, episode.EpisodeNumber);
    }

    private static bool NamesAnotherEpisode(
        TorrentContentFile file,
        TrackedEpisode episode,
        out string namedEpisode,
        out (int First, int Last)? fileRange)
    {
        var parsed = TorrentCandidateParser.Parse(Path.GetFileName(file.Name));
        fileRange = null;
        namedEpisode = parsed.SeasonNumber is not null && parsed.EpisodeNumber is not null
            ? parsed.LastEpisodeNumber is { } last
                ? $"S{parsed.SeasonNumber:00}E{parsed.EpisodeNumber:00}-E{last:00}"
                : $"S{parsed.SeasonNumber:00}E{parsed.EpisodeNumber:00}"
            : string.Empty;
        if (parsed.SeasonNumber is null || parsed.EpisodeNumber is null)
        {
            return false;
        }

        // A multi-episode file names each episode of its range (audit L1-M2).
        var first = parsed.EpisodeNumber.Value;
        var lastEpisode = parsed.LastEpisodeNumber ?? first;
        if (parsed.SeasonNumber != episode.SeasonNumber || episode.EpisodeNumber < first || episode.EpisodeNumber > lastEpisode)
        {
            return true;
        }

        fileRange = lastEpisode > first ? (first, lastEpisode) : null;
        return false;
    }

    private static SourceItem CreateEpisodeSourceItem(
        TrackedShow show,
        TrackedEpisode episode,
        AddedTorrentResult torrent,
        string sourcePath,
        AutoTorrentLinkKind linkKind,
        int? packOwnerSeasonNumber)
    {
        return new SourceItem
        {
            SourceRootFolder = torrent.SavePath,
            ParentFolder = Path.GetDirectoryName(sourcePath) ?? torrent.SavePath,
            FilePath = sourcePath,
            FileName = Path.GetFileName(sourcePath),
            ScanText = Path.GetFileNameWithoutExtension(sourcePath),
            MediaKind = MediaKind.TvEpisode,
            ParserPattern = ParserPattern.StandardTv,
            ShowTitle = show.Title,
            SeasonNumber = episode.SeasonNumber,
            EpisodeNumber = episode.EpisodeNumber,
            EpisodeTitle = episode.Title,
            MatchedTitle = show.Title,
            MatchedYear = show.FirstAirYear,
            Provider = "tmdb",
            ProviderId = show.TmdbId.ToString(),
            MatchConfidence = 100,
            MatchReason = "Accepted from Auto Torrent tracked episode.",
            RequiresManualReview = false,
            MatchAccepted = true,
            State = ItemState.Parsed,
            AutoTorrentLinkKind = linkKind,
            AutoTorrentTorrentHash = torrent.Hash,
            AutoTorrentPackOwnerSeasonNumber = packOwnerSeasonNumber,
            LastSeenUtc = DateTime.UtcNow
        };
    }

    private static SourceItem CreateMovieSourceItem(TrackedMovie movie, AddedTorrentResult torrent, string sourcePath)
    {
        return new SourceItem
        {
            SourceRootFolder = torrent.SavePath,
            ParentFolder = Path.GetDirectoryName(sourcePath) ?? torrent.SavePath,
            FilePath = sourcePath,
            FileName = Path.GetFileName(sourcePath),
            ScanText = Path.GetFileNameWithoutExtension(sourcePath),
            MediaKind = MediaKind.Movie,
            ParserPattern = ParserPattern.MovieWithYear,
            MovieTitle = movie.Title,
            MovieYear = movie.ReleaseYear,
            MatchedTitle = movie.Title,
            MatchedYear = movie.ReleaseYear,
            Provider = "tmdb",
            ProviderId = movie.TmdbId.ToString(),
            MatchConfidence = 100,
            MatchReason = "Accepted from Auto Torrent tracked movie.",
            RequiresManualReview = false,
            MatchAccepted = true,
            State = ItemState.Parsed,
            AutoTorrentLinkKind = AutoTorrentLinkKind.Movie,
            AutoTorrentTorrentHash = torrent.Hash,
            LastSeenUtc = DateTime.UtcNow
        };
    }

    private static string BuildSourcePath(AddedTorrentResult torrent, TorrentContentFile file)
    {
        return Path.Combine(torrent.SavePath, file.Name.Replace('/', Path.DirectorySeparatorChar));
    }

    private static IReadOnlyList<int> ParseCoveredSeasons(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.TryParse(item, out var season) ? season : 0)
            .Where(season => season > 0)
            .Distinct()
            .ToList();
    }

    private static void Merge(AutoTorrentLinkResult target, AutoTorrentLinkResult source)
    {
        target.LinkedCount += source.LinkedCount;
        target.SkippedCount += source.SkippedCount;
        target.RemovedCount += source.RemovedCount;
        target.ErrorCount += source.ErrorCount;
        target.Messages.AddRange(source.Messages);
    }

    private static void AddSkip(AutoTorrentLinkResult result, string message)
    {
        result.SkippedCount++;
        result.Messages.Add(message);
    }

    private sealed class SourceItemPathIndex
    {
        private readonly Dictionary<string, List<SourceItem>> _byFilePath =
            new(StringComparer.OrdinalIgnoreCase);

        public static SourceItemPathIndex From(IReadOnlyList<SourceItem> items)
        {
            var index = new SourceItemPathIndex();
            foreach (var item in items)
            {
                index.Replace(item);
            }

            return index;
        }

        public SourceItem? FindExistingLinked(string filePath)
        {
            if (!_byFilePath.TryGetValue(filePath, out var items))
            {
                return null;
            }

            return items.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                File.Exists(item.LinkedPath));
        }

        public IEnumerable<SourceItem> All() => _byFilePath.Values.SelectMany(items => items);

        public SourceItem? FindLegacyOrphan(string filePath)
        {
            if (!_byFilePath.TryGetValue(filePath, out var items))
            {
                return null;
            }

            return items.FirstOrDefault(item =>
                item.IsOrphanPackSpecial &&
                !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                File.Exists(item.LinkedPath) &&
                IsLegacyOrphanSeasonPath(item.LinkedPath));
        }

        public void Replace(SourceItem item)
        {
            if (string.IsNullOrWhiteSpace(item.FilePath))
            {
                return;
            }

            if (!_byFilePath.TryGetValue(item.FilePath, out var items))
            {
                _byFilePath[item.FilePath] = [item];
                return;
            }

            if (item.Id != 0)
            {
                var existingIndex = items.FindIndex(candidate => candidate.Id == item.Id);
                if (existingIndex >= 0)
                {
                    items[existingIndex] = item;
                    return;
                }
            }

            var samePathIndex = items.FindIndex(candidate =>
                string.Equals(candidate.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase) &&
                candidate.Id == item.Id);
            if (samePathIndex >= 0)
            {
                items[samePathIndex] = item;
                return;
            }

            items.Add(item);
        }
    }
}
