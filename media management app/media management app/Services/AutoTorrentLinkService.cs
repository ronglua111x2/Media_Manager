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

    public async Task<AutoTorrentLinkResult> LinkShowAsync(long showId, CancellationToken cancellationToken = default)
    {
        var result = new AutoTorrentLinkResult();
        var show = _databaseService.GetTrackedShow(showId);
        if (show is null)
        {
            AddSkip(result, "Tracked show was not found.");
            return result;
        }

        var episodes = _databaseService.GetTrackedEpisodes(showId);
        foreach (var season in _databaseService.GetTrackedSeasons(showId)
                     .Where(season => season.SelectedPackOwnerSeasonNumber == season.SeasonNumber &&
                                      !string.IsNullOrWhiteSpace(season.PackTorrentHash)))
        {
            Merge(result, await LinkSeasonPackCoreAsync(show, season, episodes, progress: null, cancellationToken));
        }

        var packManagedSeasons = _databaseService.GetTrackedSeasons(showId)
            .Where(season => season.ManagementMode == SeasonManagementMode.Pack)
            .Select(season => season.SeasonNumber)
            .ToHashSet();

        foreach (var episode in episodes.Where(episode =>
                     !packManagedSeasons.Contains(episode.SeasonNumber) &&
                     !string.IsNullOrWhiteSpace(episode.TorrentHash)))
        {
            await LinkEpisodeCoreAsync(show, episode, result, cancellationToken);
        }

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

    public Task<AutoTorrentLinkResult> ApplySeasonPackLinkAsync(
        SeasonPackLinkPreview preview,
        IProgress<PackLinkProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default) =>
        LinkSeasonPackFromInventoryCoreAsync(
            preview.Show,
            preview.OwnerSeason,
            _databaseService.GetTrackedEpisodes(preview.Show.Id),
            preview.Inventory,
            preview.SpecialMappings,
            progress,
            cancellationToken);

    public async Task<AutoTorrentLinkResult> LinkSeasonPackAsync(
        long showId,
        int ownerSeasonNumber,
        IProgress<PackLinkProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var preview = await PrepareSeasonPackLinkAsync(showId, ownerSeasonNumber, progress, cancellationToken: cancellationToken);
        return await ApplySeasonPackLinkAsync(preview, progress, cancellationToken);
    }

    public async Task<AutoTorrentLinkResult> LinkSeasonPackFromInventoryAsync(
        long showId,
        int ownerSeasonNumber,
        PackTorrentInventory inventory,
        CancellationToken cancellationToken = default)
    {
        var show = _databaseService.GetTrackedShow(showId);
        var season = _databaseService.GetTrackedSeasons(showId)
            .FirstOrDefault(item => item.SeasonNumber == ownerSeasonNumber);
        if (show is null || season is null)
        {
            return new AutoTorrentLinkResult
            {
                SkippedCount = 1,
                Messages = { $"Tracked pack owner season S{ownerSeasonNumber:00} was not found." }
            };
        }

        return await LinkSeasonPackFromInventoryCoreAsync(
            show,
            season,
            _databaseService.GetTrackedEpisodes(showId),
            inventory,
            resolvedSpecialMappings: null,
            progress: null,
            cancellationToken);
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

    public AutoTorrentLinkResult RemoveEpisodeLinks(long showId, int seasonNumber, int episodeNumber)
    {
        var show = _databaseService.GetTrackedShow(showId);
        var providerId = show?.TmdbId.ToString();
        return string.IsNullOrWhiteSpace(providerId)
            ? new AutoTorrentLinkResult { SkippedCount = 1, Messages = { "Tracked show was not found." } }
            : RemoveLinks(item =>
            {
                var key = GetOutputEpisodeKey(item);
                return item.MediaKind == MediaKind.TvEpisode &&
                       key == (seasonNumber, episodeNumber) &&
                       string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase);
            });
    }

    public AutoTorrentLinkResult RemoveSeasonPackLinks(long showId, int ownerSeasonNumber)
    {
        var show = _databaseService.GetTrackedShow(showId);
        var providerId = show?.TmdbId.ToString();
        return string.IsNullOrWhiteSpace(providerId)
            ? new AutoTorrentLinkResult { SkippedCount = 1, Messages = { "Tracked show was not found." } }
            : RemoveLinks(item => item.MediaKind == MediaKind.TvEpisode &&
                                  string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                                  item.AutoTorrentPackOwnerSeasonNumber == ownerSeasonNumber &&
                                  (item.AutoTorrentLinkKind == AutoTorrentLinkKind.SeasonPack ||
                                   item.IsOrphanPackSpecial));
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

        foreach (var item in items.ToList())
        {
            item.State = File.Exists(item.LinkedPath) ? ItemState.Linked : ItemState.Parsed;
            if (!File.Exists(item.LinkedPath))
            {
                item.LinkedPath = null;
                item.AutoTorrentLinkKind = null;
                item.AutoTorrentTorrentHash = null;
                item.AutoTorrentPackOwnerSeasonNumber = null;
                item.Notes = "Linked path no longer exists.";
                result.SkippedCount++;
            }

            _databaseService.UpdateSourceItem(item);
        }

        result.Messages.Add("Refreshed link status.");
        return result;
    }

    public AutoTorrentLinkResult ResetEpisodeForRedownload(long showId, int seasonNumber, int episodeNumber)
    {
        var result = new AutoTorrentLinkResult();

        // Remove hardlinks and reset SourceItem link fields (handles the case where file still exists)
        var unlinkResult = RemoveEpisodeLinks(showId, seasonNumber, episodeNumber);
        result.LinkedCount += unlinkResult.LinkedCount;
        result.SkippedCount += unlinkResult.SkippedCount;
        result.Messages.AddRange(unlinkResult.Messages);

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
            var episodeItems = _databaseService.GetSourceItems()
                .Where(item =>
                    item.MediaKind == MediaKind.TvEpisode &&
                    string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                .Where(item =>
                {
                    var key = GetOutputEpisodeKey(item);
                    return key == (seasonNumber, episodeNumber);
                })
                .ToList();

            foreach (var item in episodeItems)
            {
                _databaseService.DeleteSourceItem(item.Id);
                result.Messages.Add($"Removed source item: {item.FileName}");
            }
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

    public AutoTorrentLinkResult ResetSeasonPackForRedownload(long showId, int ownerSeasonNumber)
    {
        var result = new AutoTorrentLinkResult();

        // Capture pack SourceItems before unlink clears their link fields.
        var show = _databaseService.GetTrackedShow(showId);
        var providerId = show?.TmdbId.ToString();
        var packItems = string.IsNullOrWhiteSpace(providerId)
            ? []
            : _databaseService.GetSourceItems()
                .Where(item =>
                    item.MediaKind == MediaKind.TvEpisode &&
                    string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                    item.AutoTorrentPackOwnerSeasonNumber == ownerSeasonNumber &&
                    (item.AutoTorrentLinkKind == AutoTorrentLinkKind.SeasonPack ||
                     item.IsOrphanPackSpecial))
                .ToList();

        var unlinkResult = RemoveSeasonPackLinks(showId, ownerSeasonNumber);
        result.LinkedCount += unlinkResult.LinkedCount;
        result.SkippedCount += unlinkResult.SkippedCount;
        result.Messages.AddRange(unlinkResult.Messages);

        foreach (var item in packItems)
        {
            _databaseService.DeleteSourceItem(item.Id);
            result.Messages.Add($"Removed source item: {item.FileName}");
        }

        _databaseService.ClearTrackedSeasonSelectedPack(showId, ownerSeasonNumber);

        var packOrders = _databaseService.GetTorrentCartOrders(MediaKind.TvEpisode, showId)
            .Where(order => order.EpisodeId is null && order.SeasonNumber == ownerSeasonNumber)
            .ToList();
        foreach (var order in packOrders)
        {
            _databaseService.DeleteTorrentCartOrder(order.Id);
        }

        result.Messages.Add($"Reset season S{ownerSeasonNumber:00} pack: download state cleared.");
        return result;
    }

    public AutoTorrentLinkResult ResetMovieForRedownload(long movieId)
    {
        var result = new AutoTorrentLinkResult();

        var unlinkResult = RemoveMovieLinks(movieId);
        result.LinkedCount += unlinkResult.LinkedCount;
        result.SkippedCount += unlinkResult.SkippedCount;
        result.Messages.AddRange(unlinkResult.Messages);

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

        LinkSourceItem(
            CreateEpisodeSourceItem(
                show,
                episode,
                torrent,
                BuildSourcePath(torrent, selectedFile),
                AutoTorrentLinkKind.Episode,
                packOwnerSeasonNumber: null),
            result);
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
        var tree = PackFolderTreeAnalyzer.Analyze(fileTuples.Select(file => file.Name).ToList());
        var specialsEpisodes = episodes
            .Where(episode => episode.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            .ToList();

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
                ownerSeasonNumber: ownerSeason.SeasonNumber);

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

        var resolvedSpecialMappings = await _specialMappingOrchestrator.ResolveAsync(
            fileTuples,
            tree,
            specialsEpisodes,
            show.DisplayTitle,
            show.TmdbId,
            ownerSeason.PackTorrentHash!,
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
            ownerSeasonNumber: ownerSeason.SeasonNumber);

        return new SeasonPackLinkPreview
        {
            Show = show,
            OwnerSeason = ownerSeason,
            Inventory = inventory,
            SpecialMappings = resolvedSpecialMappings,
            RequiresReview = useGeminiForSpecials && resolvedSpecialMappings.UsedGemini,
            RegularEpisodeCount = inventory.Files.Count(file => file.Classification == PackFileClassification.RegularEpisode),
            MatchedSpecialCount = inventory.Files.Count(file => file.Classification == PackFileClassification.MatchedSpecial),
            OrphanExtraCount = inventory.Files.Count(file => file.Classification == PackFileClassification.UnmatchedExtra),
            RegularEpisodesBySeason = regularEpisodesBySeason
        };
    }

    private async Task<AutoTorrentLinkResult> LinkSeasonPackCoreAsync(
        TrackedShow show,
        TrackedSeason ownerSeason,
        IReadOnlyList<TrackedEpisode> episodes,
        IProgress<PackLinkProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var preview = await PrepareSeasonPackLinkCoreAsync(
            show,
            ownerSeason,
            episodes,
            progress,
            useGeminiForSpecials: true,
            bypassCache: false,
            cancellationToken);
        return await ApplySeasonPackLinkAsync(preview, progress, cancellationToken);
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

        foreach (var entry in inventory.Files.Where(file => file.Classification == PackFileClassification.Movie))
        {
            AddSkip(result, $"{show.DisplayTitle}: skipped movie pack file '{entry.FileName}'.");
        }

        foreach (var entry in inventory.Files.Where(file => file.Classification == PackFileClassification.Skipped))
        {
            AddSkip(result, $"{show.DisplayTitle}: skipped unmatched pack file '{entry.FileName}'.");
        }

        foreach (var entry in linkableEntries)
        {
            var sourcePath = BuildSourcePath(torrent, new TorrentContentFile
            {
                Name = entry.RelativePath,
                Size = 0,
                Progress = 1
            });

            switch (entry.Classification)
            {
                case PackFileClassification.UnmatchedExtra:
                    if (LinkOrphanPackItem(show, torrent, sourcePath, ownerSeason.SeasonNumber, result))
                    {
                        orphansLinked++;
                    }

                    break;
                case PackFileClassification.MatchedSpecial when entry.MatchedSeasonNumber is not null &&
                                                                entry.MatchedEpisodeNumber is not null &&
                                                                episodesByKey.TryGetValue(
                                                                    (entry.MatchedSeasonNumber.Value, entry.MatchedEpisodeNumber.Value),
                                                                    out var specialEpisode):
                    LinkSourceItem(
                        CreateEpisodeSourceItem(
                            show,
                            specialEpisode,
                            torrent,
                            sourcePath,
                            AutoTorrentLinkKind.SeasonPack,
                            ownerSeason.SeasonNumber),
                        result);
                    specialsMatched++;
                    break;
                case PackFileClassification.RegularEpisode when entry.MatchedSeasonNumber is not null &&
                                                                  entry.MatchedEpisodeNumber is not null &&
                                                                  episodesByKey.TryGetValue(
                                                                      (entry.MatchedSeasonNumber.Value, entry.MatchedEpisodeNumber.Value),
                                                                      out var episode):
                    LinkSourceItem(
                        CreateEpisodeSourceItem(
                            show,
                            episode,
                            torrent,
                            sourcePath,
                            AutoTorrentLinkKind.SeasonPack,
                            ownerSeason.SeasonNumber),
                        result);
                    regularLinked++;
                    break;
            }
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
        AutoTorrentLinkResult result)
    {
        var beforeLinked = result.LinkedCount;
        LinkSourceItem(
            CreateOrphanPackSourceItem(show, torrent, sourcePath, packOwnerSeasonNumber),
            result);
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

    private void LinkSourceItem(SourceItem sourceItem, AutoTorrentLinkResult result)
    {
        TryMigrateLegacyOrphanPath(sourceItem, result);

        if (TryGetExistingLinkedItem(sourceItem, out var existingItem) && existingItem is not null)
        {
            existingItem.AutoTorrentLinkKind = sourceItem.AutoTorrentLinkKind;
            existingItem.AutoTorrentTorrentHash = sourceItem.AutoTorrentTorrentHash;
            existingItem.AutoTorrentPackOwnerSeasonNumber = sourceItem.AutoTorrentPackOwnerSeasonNumber;
            existingItem.IsOrphanPackSpecial = sourceItem.IsOrphanPackSpecial;
            existingItem.LastSeenUtc = DateTime.UtcNow;
            _databaseService.UpdateSourceItem(existingItem);
            AddSkip(result, $"{sourceItem.DisplayTitle}: already linked.");
            return;
        }

        if (_hardlinkService.CreateHardLink(sourceItem, out var linkedPath, out var errorMessage))
        {
            sourceItem.State = ItemState.Linked;
            sourceItem.LinkedPath = linkedPath;
            sourceItem.Notes = null;
            _databaseService.UpdateSourceItem(sourceItem);
            _eventHub.PublishHardlinkCreated(sourceItem, linkedPath!);
            result.LinkedCount++;
            result.Messages.Add($"Linked {sourceItem.FileName}");
            return;
        }

        result.ErrorCount++;
        result.Messages.Add($"{sourceItem.FileName}: {errorMessage}");
    }

    private void TryMigrateLegacyOrphanPath(SourceItem sourceItem, AutoTorrentLinkResult result)
    {
        if (!sourceItem.IsOrphanPackSpecial)
        {
            return;
        }

        var existingOrphan = _databaseService.GetSourceItems().FirstOrDefault(item =>
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
            result.Messages.Add($"Migrated orphan {existingOrphan.FileName} from Season 00 to extras/.");
            return;
        }

        _logger.Warning($"Could not migrate legacy orphan path for {existingOrphan.FileName}: {errorMessage}", LogTarget.All);
    }

    private static bool IsLegacyOrphanSeasonPath(string linkedPath)
    {
        var normalized = linkedPath.Replace('\\', '/');
        return normalized.Contains("/Season 00/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/Season 0/", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetExistingLinkedItem(SourceItem sourceItem, out SourceItem? existingItem)
    {
        existingItem = _databaseService.GetSourceItems().FirstOrDefault(item =>
            string.Equals(item.FilePath, sourceItem.FilePath, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.LinkedPath) &&
            File.Exists(item.LinkedPath));
        return existingItem is not null;
    }

    private AutoTorrentLinkResult RemoveLinks(Func<SourceItem, bool> predicate)
    {
        var result = new AutoTorrentLinkResult();
        foreach (var item in _databaseService.GetSourceItems().Where(predicate).ToList())
        {
            if (string.IsNullOrWhiteSpace(item.LinkedPath))
            {
                continue;
            }

            if (item.IsExternalImport)
            {
                _databaseService.DeleteSourceItem(item.Id);
                result.RemovedCount++;
                result.Messages.Add($"Removed imported library record for {item.FileName}.");
                continue;
            }

            if (item.IsOrphanPackSpecial)
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
                item.AutoTorrentLinkKind = null;
                item.AutoTorrentTorrentHash = null;
                item.AutoTorrentPackOwnerSeasonNumber = null;
                item.Notes = null;
                _databaseService.UpdateSourceItem(item);
                result.RemovedCount++;
                continue;
            }

            result.ErrorCount++;
            result.Messages.Add($"{item.FileName}: {removeErrorMessage}");
        }

        return result;
    }

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
            .Where(file => file.IsVideoFile && file.IsComplete)
            .ToList();
    }

    private static TorrentContentFile? SelectEpisodeFile(IReadOnlyList<TorrentContentFile> files, TrackedEpisode episode)
    {
        if (files.Count == 1)
        {
            return files[0];
        }

        return files.FirstOrDefault(file =>
        {
            var parsed = TorrentCandidateParser.Parse(file.Name);
            return parsed.SeasonNumber == episode.SeasonNumber && parsed.EpisodeNumber == episode.EpisodeNumber;
        });
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
}
