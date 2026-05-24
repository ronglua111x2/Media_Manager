using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class TrackedShowService : ITrackedShowService
{
    private readonly IDatabaseService _databaseService;
    private readonly ITmdbShowCatalogService _catalogService;
    private readonly IAppLogger _logger;

    public TrackedShowService(IDatabaseService databaseService, ITmdbShowCatalogService catalogService, IAppLogger logger)
    {
        _databaseService = databaseService;
        _catalogService = catalogService;
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
        RefreshAvailabilityCore(showId);
        var show = _databaseService.GetTrackedShow(showId) ?? throw new InvalidOperationException("Tracked show was not saved.");
        _logger.Info($"Tracked show added: {show.DisplayTitle}, Episodes={show.TotalEpisodes}", LogTarget.All);
        return show;
    }

    public async Task<TrackedShow> RefreshShowAsync(TrackedShow show, CancellationToken cancellationToken = default)
    {
        var details = await _catalogService.GetTvShowDetailsAsync(show.TmdbId, cancellationToken);
        var showId = ImportShow(details, show.PreferredQuality);
        RefreshAvailabilityCore(showId);
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
        foreach (var show in _databaseService.GetTrackedShows())
        {
            RefreshAvailabilityCore(show.Id);
        }
    }

    public void RefreshAvailability(long showId)
    {
        RefreshAvailabilityCore(showId);
    }

    public void UpdateWanted(long episodeId, bool isWanted)
    {
        _databaseService.UpdateTrackedEpisodeWanted(episodeId, isWanted);
    }

    public void UpdateSeasonDownloadFolder(long showId, int seasonNumber, string? downloadFolder)
    {
        _databaseService.UpdateTrackedSeasonDownloadFolder(showId, seasonNumber, downloadFolder);
        _logger.Info(
            $"Updated season download folder for show id={showId} S{seasonNumber:00}: '{downloadFolder ?? "<default>"}'",
            LogTarget.All);
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
            torrentHash,
            string.Empty,
            "Removed from qBittorrent",
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
            PreferredQuality = existing?.PreferredQuality ?? preferredQuality,
            PreferredAudioCodec = existing?.PreferredAudioCodec ?? string.Empty,
            MinimumSeeders = existing?.MinimumSeeders ?? 0
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

    private void RefreshAvailabilityCore(long showId)
    {
        var show = _databaseService.GetTrackedShow(showId);
        if (show is null)
        {
            return;
        }

        var availableEpisodes = GetAvailableEpisodeKeys(show.TmdbId);
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

    private HashSet<(int SeasonNumber, int EpisodeNumber)> GetAvailableEpisodeKeys(int tmdbId)
    {
        var providerId = tmdbId.ToString();
        return _databaseService.GetSourceItems()
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
}
