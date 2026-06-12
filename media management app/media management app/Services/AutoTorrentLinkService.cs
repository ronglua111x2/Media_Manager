using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class AutoTorrentLinkService : IAutoTorrentLinkService
{
    private readonly IDatabaseService _databaseService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IHardlinkService _hardlinkService;
    private readonly IAppLogger _logger;

    public AutoTorrentLinkService(
        IDatabaseService databaseService,
        IQbittorrentClient qbittorrentClient,
        IHardlinkService hardlinkService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _qbittorrentClient = qbittorrentClient;
        _hardlinkService = hardlinkService;
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
            Merge(result, await LinkSeasonPackCoreAsync(show, season, episodes, cancellationToken));
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

    public async Task<AutoTorrentLinkResult> LinkSeasonPackAsync(long showId, int ownerSeasonNumber, CancellationToken cancellationToken = default)
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

        return await LinkSeasonPackCoreAsync(show, season, _databaseService.GetTrackedEpisodes(showId), cancellationToken);
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
                                  item.AutoTorrentLinkKind == AutoTorrentLinkKind.SeasonPack &&
                                  item.AutoTorrentPackOwnerSeasonNumber == ownerSeasonNumber &&
                                  string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
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

    private async Task<AutoTorrentLinkResult> LinkSeasonPackCoreAsync(
        TrackedShow show,
        TrackedSeason ownerSeason,
        IReadOnlyList<TrackedEpisode> episodes,
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

        var coveredSeasons = ParseCoveredSeasons(ownerSeason.SelectedPackCoveredSeasons).ToHashSet();
        if (coveredSeasons.Count == 0)
        {
            coveredSeasons.Add(ownerSeason.SeasonNumber);
        }

        var episodesByKey = episodes.ToDictionary(episode => (episode.SeasonNumber, episode.EpisodeNumber));
        var files = await GetCompletedVideoFilesAsync(torrent, cancellationToken);
        foreach (var file in files)
        {
            var parsed = TorrentCandidateParser.Parse(file.Name);
            if (parsed.SeasonNumber is null ||
                parsed.EpisodeNumber is null ||
                !coveredSeasons.Contains(parsed.SeasonNumber.Value) ||
                !episodesByKey.TryGetValue((parsed.SeasonNumber.Value, parsed.EpisodeNumber.Value), out var episode))
            {
                AddSkip(result, $"{show.DisplayTitle}: skipped unmatched pack file '{file.Name}'.");
                continue;
            }

            LinkSourceItem(
                CreateEpisodeSourceItem(
                    show,
                    episode,
                    torrent,
                    BuildSourcePath(torrent, file),
                    AutoTorrentLinkKind.SeasonPack,
                    ownerSeason.SeasonNumber),
                result);
        }

        return result;
    }

    private void LinkSourceItem(SourceItem sourceItem, AutoTorrentLinkResult result)
    {
        if (TryGetExistingLinkedItem(sourceItem, out var existingItem) && existingItem is not null)
        {
            existingItem.AutoTorrentLinkKind = sourceItem.AutoTorrentLinkKind;
            existingItem.AutoTorrentTorrentHash = sourceItem.AutoTorrentTorrentHash;
            existingItem.AutoTorrentPackOwnerSeasonNumber = sourceItem.AutoTorrentPackOwnerSeasonNumber;
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
            result.LinkedCount++;
            result.Messages.Add($"Linked {sourceItem.FileName}");
            return;
        }

        result.ErrorCount++;
        result.Messages.Add($"{sourceItem.FileName}: {errorMessage}");
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

            if (_hardlinkService.RemoveHardLink(item, out _, out var errorMessage))
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
            result.Messages.Add($"{item.FileName}: {errorMessage}");
        }

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
