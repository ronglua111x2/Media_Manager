using media_management_app.Models;

namespace media_management_app.Services;

public interface ITrackedShowService
{
    Task<IReadOnlyList<TmdbShowSearchResult>> SearchShowsAsync(string query, CancellationToken cancellationToken = default);

    Task<TrackedShow> AddShowAsync(TmdbShowSearchResult result, CancellationToken cancellationToken = default);

    Task<TrackedShow> ImportShowByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken = default);

    Task<TrackedShow> RefreshShowAsync(TrackedShow show, CancellationToken cancellationToken = default);

    IReadOnlyList<TrackedShow> GetShows();

    IReadOnlyList<TrackedEpisode> GetEpisodes(long showId);

    IReadOnlyList<TrackedSeason> GetSeasons(long showId);

    void RefreshAvailability();

    void RefreshAvailability(long showId);

    void UpdateSeasonDownloadFolder(long showId, int seasonNumber, string? downloadFolder);

    void UpdateSeasonPackMode(long showId, int seasonNumber, Common.SeasonManagementMode mode);

    void UpdateSeasonHidden(long showId, int seasonNumber, bool isHidden);

    void UpdateSeasonSelectedPack(long showId, int ownerSeasonNumber, SeasonPackCandidate candidate);

    void ClearSeasonSelectedPacksForSeasons(long showId, IReadOnlyList<int> seasonNumbers);

    void ClearSeasonSelectedPack(long showId, int ownerSeasonNumber);

    void UpdateSeasonPackTorrent(long showId, int ownerSeasonNumber, AddedTorrentResult torrent);

    void MarkSeasonPackTorrentRemoved(long showId, int ownerSeasonNumber, string torrentHash);

    void UpdateTorrentState(long episodeId, AddedTorrentResult torrent);

    void MarkTorrentRemoved(long episodeId, string torrentHash);

    void UpdateSelectedCandidate(long episodeId, EpisodeFetchCandidate candidate);

    void UpdatePreferredQuality(long showId, string preferredQuality);

    void UpdatePreferences(long showId, string preferredQuality, string preferredAudioCodec, int minimumSeeders);

    void UpdateRecipe(long showId, string? recipeId);

    void UpdatePackRecipe(long showId, string? packRecipeId);

    void UpdateSeriesStatus(long showId, Common.ShowSeriesStatus seriesStatus);

    void SetAlternativeTitleExcludedFromSearch(long showId, string title, bool excluded);

    IReadOnlyList<TrackedShow> GetAutoTrackedShows();

    void SetAutoTrackCheckpoint(long showId, int fromSeason, int fromEpisode, string? downloadFolder = null, bool autoReconcileAndLink = true);

    void UpdateAutoTrackDownloadFolder(long showId, string? downloadFolder);

    void UpdateAutoTrackReconcileAndLink(long showId, bool autoReconcileAndLink);

    void UpdateAutoTrackScheduleOverrides(long showId, DayOfWeek? anchorDayOfWeek, string? anchorTimeLocal, bool clearOverrides);

    void UpdateAutoTrackQualityOverrides(
        long showId,
        string? minQuality,
        int? minSeeders,
        int? minFileSizeMb,
        int? maxFileSizeMb,
        string? allowedQualities,
        bool clearOverrides);

    void StopAutoTrack(long showId);
}
