using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class TrackedShowService : ITrackedShowService
{
    private readonly IDatabaseService _databaseService;
    private readonly ITmdbShowCatalogService _catalogService;
    private readonly IPosterImageService _posterImageService;
    private readonly IAppLogger _logger;

    public TrackedShowService(
        IDatabaseService databaseService,
        ITmdbShowCatalogService catalogService,
        IPosterImageService posterImageService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _catalogService = catalogService;
        _posterImageService = posterImageService;
        _logger = logger;
    }

    public Task<IReadOnlyList<TmdbShowSearchResult>> SearchShowsAsync(string query, CancellationToken cancellationToken = default)
    {
        return _catalogService.SearchTvShowsAsync(query, cancellationToken);
    }

    public async Task<TrackedShow> AddShowAsync(TmdbShowSearchResult result, CancellationToken cancellationToken = default)
    {
        var details = await _catalogService.GetTvShowDetailsAsync(result.TmdbId, cancellationToken);
        var showId = ImportShow(details, "1080p");
        await CachePosterAsync(details.TmdbId, details.PosterPath, cancellationToken);
        RefreshAvailability(showId);
        var show = _databaseService.GetTrackedShow(showId) ?? throw new InvalidOperationException("Tracked show was not saved.");
        _logger.Info($"Tracked show added: {show.DisplayTitle}, Episodes={show.TotalEpisodes}", LogTarget.All);
        return show;
    }

    public async Task<TrackedShow> ImportShowByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        var details = await _catalogService.GetTvShowDetailsAsync(tmdbId, cancellationToken);
        var showId = ImportShow(details, "1080p");
        await CachePosterAsync(details.TmdbId, details.PosterPath, cancellationToken);
        RefreshAvailability(showId);
        var show = _databaseService.GetTrackedShow(showId) ?? throw new InvalidOperationException("Tracked show was not imported.");
        _logger.Info($"Tracked show imported from existing media: {show.DisplayTitle}, Episodes={show.TotalEpisodes}", LogTarget.All);
        return show;
    }

    public async Task<TrackedShow> RefreshShowAsync(TrackedShow show, CancellationToken cancellationToken = default)
    {
        var details = await _catalogService.GetTvShowDetailsAsync(show.TmdbId, cancellationToken);
        var showId = ImportShow(details, show.PreferredQuality);
        await CachePosterAsync(details.TmdbId, details.PosterPath, cancellationToken);
        RefreshAvailability(showId);
        var refreshed = _databaseService.GetTrackedShow(showId) ?? throw new InvalidOperationException("Tracked show was not refreshed.");
        _logger.Info($"Tracked show refreshed: {refreshed.DisplayTitle}, Episodes={refreshed.TotalEpisodes}", LogTarget.All);
        return refreshed;
    }

    public IReadOnlyList<TrackedShow> GetShows()
    {
        return _databaseService.GetTrackedShows();
    }

    public IReadOnlyList<TrackedEpisode> GetEpisodes(long showId)
    {
        return _databaseService.GetTrackedEpisodes(showId);
    }

    public IReadOnlyList<TrackedSeason> GetSeasons(long showId)
    {
        return _databaseService.GetTrackedSeasons(showId);
    }

    public void RefreshAvailability()
    {
        var sourceItems = _databaseService.GetSourceItems();
        RefreshAvailability(sourceItems);
    }

    public void RefreshAvailability(IReadOnlyList<SourceItem> sourceItems)
    {
        foreach (var show in _databaseService.GetTrackedShows())
        {
            RefreshAvailabilityCore(show.Id, sourceItems);
        }
    }

    public void RefreshAvailability(long showId)
    {
        RefreshAvailabilityCore(showId, _databaseService.GetSourceItems());
    }

    public void RefreshAvailability(long showId, IReadOnlyList<SourceItem> sourceItems)
    {
        RefreshAvailabilityCore(showId, sourceItems);
    }

    public void UpdateSeasonDownloadFolder(long showId, int seasonNumber, string? downloadFolder)
    {
        _databaseService.UpdateTrackedSeasonDownloadFolder(showId, seasonNumber, downloadFolder);
        _logger.Info(
            $"Updated season download folder for show id={showId} S{seasonNumber:00}: '{downloadFolder ?? "<default>"}'",
            LogTarget.All);
    }

    public void UpdateSeasonPackMode(long showId, int seasonNumber, SeasonManagementMode mode)
    {
        _databaseService.UpdateTrackedSeasonPackMode(showId, seasonNumber, mode);
    }

    public void UpdateSeasonHidden(long showId, int seasonNumber, bool isHidden)
    {
        _databaseService.UpdateTrackedSeasonHidden(showId, seasonNumber, isHidden);
    }

    public void UpdateSeasonSelectedPack(long showId, int ownerSeasonNumber, SeasonPackCandidate candidate)
    {
        _databaseService.UpdateTrackedSeasonSelectedPack(showId, ownerSeasonNumber, candidate);
        _logger.Info($"Saved season pack for show id={showId} S{ownerSeasonNumber:00}: '{candidate.FileName}'", LogTarget.All);
    }

    public void ClearSeasonSelectedPacksForSeasons(long showId, IReadOnlyList<int> seasonNumbers)
    {
        _databaseService.ClearTrackedSeasonSelectedPacksForSeasons(showId, seasonNumbers);
        _logger.Info($"Cleared overlapping season packs for show id={showId}. Seasons={string.Join(",", seasonNumbers)}", LogTarget.All);
    }

    public void ClearSeasonSelectedPack(long showId, int ownerSeasonNumber)
    {
        _databaseService.ClearTrackedSeasonSelectedPack(showId, ownerSeasonNumber);
        _logger.Info($"Cleared season pack for show id={showId} S{ownerSeasonNumber:00}", LogTarget.All);
    }

    public void UpdateSeasonPackTorrent(long showId, int ownerSeasonNumber, AddedTorrentResult torrent)
    {
        _databaseService.UpdateTrackedSeasonPackTorrent(showId, ownerSeasonNumber, torrent);
    }

    public void MarkSeasonPackTorrentRemoved(long showId, int ownerSeasonNumber, string torrentHash)
    {
        _databaseService.MarkTrackedSeasonPackTorrentRemoved(showId, ownerSeasonNumber, torrentHash);
    }

    public void UpdateTorrentState(long episodeId, AddedTorrentResult torrent)
    {
        _databaseService.UpdateTrackedEpisodeTorrent(
            episodeId,
            torrent.Hash,
            torrent.Name,
            torrent.IsComplete ? "Downloaded" : torrent.State,
            torrent.Progress);
    }

    public void MarkTorrentRemoved(long episodeId, string torrentHash)
    {
        _databaseService.UpdateTrackedEpisodeTorrent(
            episodeId,
            string.Empty,
            string.Empty,
            string.Empty,
            0);
    }

    public void UpdateSelectedCandidate(long episodeId, EpisodeFetchCandidate candidate)
    {
        _databaseService.UpdateTrackedEpisodeSelectedCandidate(episodeId, candidate);
        _logger.Info($"Saved selected candidate for episode id={episodeId}: '{candidate.FileName}'", LogTarget.All);
    }

    public void UpdatePreferredQuality(long showId, string preferredQuality)
    {
        _databaseService.UpdateTrackedShowPreferredQuality(showId, preferredQuality);
    }

    public void UpdatePreferences(long showId, string preferredQuality, string preferredAudioCodec, int minimumSeeders)
    {
        _databaseService.UpdateTrackedShowPreferences(showId, preferredQuality, preferredAudioCodec, minimumSeeders);
        _logger.Info(
            $"Updated Auto Torrent preferences for show id={showId}: Quality='{preferredQuality}', Audio='{preferredAudioCodec}', MinimumSeeders={minimumSeeders}",
            LogTarget.All);
    }

    public void UpdateRecipe(long showId, string? recipeId)
    {
        _databaseService.UpdateTrackedShowRecipe(showId, recipeId);
        _logger.Info($"Updated recipe assignment for show id={showId}: {recipeId ?? "<default>"}", LogTarget.All);
    }

    public void UpdatePackRecipe(long showId, string? packRecipeId)
    {
        _databaseService.UpdateTrackedShowPackRecipe(showId, packRecipeId);
        _logger.Info($"Updated pack recipe assignment for show id={showId}: {packRecipeId ?? "<default>"}", LogTarget.All);
    }

    public void UpdateSeriesStatus(long showId, ShowSeriesStatus seriesStatus)
    {
        _databaseService.UpdateTrackedShowSeriesStatus(showId, seriesStatus);
        _logger.Info($"Updated series status for show id={showId}: {seriesStatus}", LogTarget.All);
    }

    public void SetAlternativeTitleExcludedFromSearch(long showId, string title, bool excluded)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var show = _databaseService.GetTrackedShow(showId)
            ?? throw new InvalidOperationException($"Tracked show id={showId} was not found.");
        var excludedTitles = show.ExcludedFromSearchAlternativeTitles.ToList();
        var normalizedTitle = title.Trim();
        if (excluded)
        {
            if (!excludedTitles.Any(existing => string.Equals(existing, normalizedTitle, StringComparison.OrdinalIgnoreCase)))
            {
                excludedTitles.Add(normalizedTitle);
            }
        }
        else
        {
            excludedTitles.RemoveAll(existing => string.Equals(existing, normalizedTitle, StringComparison.OrdinalIgnoreCase));
        }

        var json = TrackedShow.SerializeAlternativeTitles(excludedTitles);
        _databaseService.UpdateTrackedShowExcludedAlternativeTitles(showId, json);
        _logger.Info(
            $"Alternative title '{normalizedTitle}' {(excluded ? "excluded from" : "included in")} recipe search for show id={showId}.",
            LogTarget.All);
    }

    private long ImportShow(TmdbShowDetails details, string preferredQuality)
    {
        var existing = _databaseService.GetTrackedShowByTmdbId(details.TmdbId);
        var showId = _databaseService.UpsertTrackedShow(new TrackedShow
        {
            TmdbId = details.TmdbId,
            Title = details.Title,
            FirstAirYear = details.FirstAirYear,
            Overview = details.Overview,
            PosterPath = details.PosterPath,
            AlternativeTitlesJson = TrackedShow.SerializeAlternativeTitles(details.AlternativeTitles),
            ExcludedAlternativeTitlesJson = existing?.ExcludedAlternativeTitlesJson,
            RecipeId = existing?.RecipeId,
            PackRecipeId = existing?.PackRecipeId,
            PreferredQuality = existing?.PreferredQuality ?? preferredQuality,
            PreferredAudioCodec = existing?.PreferredAudioCodec ?? string.Empty,
            MinimumSeeders = existing?.MinimumSeeders ?? 0,
            SeriesStatus = details.SeriesStatus
        });

        foreach (var season in details.Seasons)
        {
            _databaseService.UpsertTrackedSeason(new TrackedSeason
            {
                ShowId = showId,
                SeasonNumber = season.SeasonNumber,
                EpisodeCount = season.EpisodeCount,
                DownloadFolder = existing is null
                    ? null
                    : _databaseService.GetTrackedSeasons(existing.Id)
                        .FirstOrDefault(item => item.SeasonNumber == season.SeasonNumber)?.DownloadFolder
            });

            if (season.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            {
                _databaseService.UpdateTrackedSeasonPackMode(showId, season.SeasonNumber, SeasonManagementMode.Episode);
            }

            foreach (var episode in season.Episodes)
            {
                _databaseService.UpsertTrackedEpisode(new TrackedEpisode
                {
                    ShowId = showId,
                    SeasonNumber = episode.SeasonNumber,
                    EpisodeNumber = episode.EpisodeNumber,
                    Title = episode.Title,
                    AirDate = episode.AirDate
                });
            }
        }

        return showId;
    }

    private void RefreshAvailabilityCore(long showId, IReadOnlyList<SourceItem> sourceItems)
    {
        var show = _databaseService.GetTrackedShow(showId);
        if (show is null)
        {
            return;
        }

        var availableEpisodes = GetAvailableEpisodeKeys(show.TmdbId, sourceItems);
        foreach (var episode in _databaseService.GetTrackedEpisodes(showId))
        {
            var availability = availableEpisodes.Contains((episode.SeasonNumber, episode.EpisodeNumber))
                ? EpisodeAvailability.Available
                : EpisodeAvailability.Missing;
            if (episode.Availability != availability)
            {
                _databaseService.UpdateTrackedEpisodeAvailability(episode.Id, availability);
            }
        }
    }

    private static HashSet<(int SeasonNumber, int EpisodeNumber)> GetAvailableEpisodeKeys(
        int tmdbId,
        IReadOnlyList<SourceItem> sourceItems)
    {
        var providerId = tmdbId.ToString();
        return sourceItems
            .Where(item =>
                item.MediaKind == MediaKind.TvEpisode &&
                item.MatchAccepted &&
                item.State is not ItemState.Deleted and not ItemState.Ignored &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .Select(GetOutputEpisodeKey)
            .Where(key => key is not null)
            .Select(key => key!.Value)
            .ToHashSet();
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

    public IReadOnlyList<TrackedShow> GetAutoTrackedShows()
    {
        return _databaseService.GetAutoTrackedShows();
    }

    public void SetAutoTrackCheckpoint(long showId, int fromSeason, int fromEpisode, string? downloadFolder = null, bool autoReconcileAndLink = true)
    {
        _databaseService.UpdateTrackedShowAutoTrackSettings(
            showId,
            fromSeason,
            fromEpisode,
            downloadFolder,
            autoReconcileAndLink);
        _logger.Info(
            $"Auto-track enabled for show {showId} from S{fromSeason:00}E{fromEpisode:00}, folder='{downloadFolder ?? "<default>"}', reconcile={autoReconcileAndLink}.",
            LogTarget.All);
    }

    public void UpdateAutoTrackDownloadFolder(long showId, string? downloadFolder)
    {
        _databaseService.UpdateTrackedShowAutoTrackDownloadFolder(showId, downloadFolder);
        _logger.Info($"Auto-track download folder updated for show {showId}: '{downloadFolder ?? "<cleared>"}'.", LogTarget.All);
    }

    public void UpdateAutoTrackReconcileAndLink(long showId, bool autoReconcileAndLink)
    {
        _databaseService.UpdateTrackedShowAutoTrackReconcileAndLink(showId, autoReconcileAndLink);
        _logger.Info($"Auto-track reconcile/link for show {showId}: {autoReconcileAndLink}.", LogTarget.All);
    }

    public void UpdateAutoTrackScheduleOverrides(long showId, DayOfWeek? anchorDayOfWeek, string? anchorTimeLocal, bool clearOverrides)
    {
        _databaseService.UpdateTrackedShowAutoTrackScheduleOverrides(showId, anchorDayOfWeek, anchorTimeLocal, clearOverrides);
        _logger.Info($"Auto-track schedule overrides updated for show {showId}. Clear={clearOverrides}.", LogTarget.All);
    }

    public void UpdateAutoTrackQualityOverrides(
        long showId,
        string? minQuality,
        int? minSeeders,
        int? minFileSizeMb,
        int? maxFileSizeMb,
        string? allowedQualities,
        bool clearOverrides)
    {
        _databaseService.UpdateTrackedShowAutoTrackQualityOverrides(
            showId,
            minQuality,
            minSeeders,
            minFileSizeMb,
            maxFileSizeMb,
            allowedQualities,
            clearOverrides);
        _logger.Info($"Auto-track quality overrides updated for show {showId}. Clear={clearOverrides}.", LogTarget.All);
    }

    public void StopAutoTrack(long showId)
    {
        _databaseService.UpdateTrackedShowAutoTrack(showId, null, null);
        _logger.Info($"Auto-track disabled for show {showId}.", LogTarget.All);
    }

    private Task CachePosterAsync(int tmdbId, string? posterPath, CancellationToken cancellationToken)
    {
        return _posterImageService.EnsureCachedAsync(MediaKind.TvEpisode, tmdbId, posterPath, cancellationToken);
    }
}
