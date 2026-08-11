using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface IDatabaseService
{
    void Initialize(string stateFolder);

    IReadOnlyList<SourceItem> GetSourceItems();

    void UpsertSourceItem(SourceItem item);

    void UpsertSourceItems(IEnumerable<SourceItem> items);

    void UpdateSourceItem(SourceItem item);

    int MarkMissingSourceItems(IEnumerable<string> sourceFolders, IEnumerable<string> seenFilePaths);

    int DeleteSourceItemsByState(ItemState state);

    int DeleteSourceItem(long id);

    SeriesMapping? GetSeriesMapping(string parsedTitle, ParserPattern parserPattern);

    void UpsertSeriesMapping(SeriesMapping mapping);

    IReadOnlyList<TrackedShow> GetTrackedShows();

    IReadOnlyList<TrackedShow> GetTrackedShowsBySeriesStatus(ShowSeriesStatus seriesStatus);

    IReadOnlyList<TrackedShow> GetAutoTrackedShows();

    void UpdateTrackedShowAutoTrack(long showId, int? fromSeason, int? fromEpisode);

    void UpdateTrackedShowAutoTrackSettings(
        long showId,
        int? fromSeason,
        int? fromEpisode,
        string? downloadFolder,
        bool? autoReconcileAndLink);

    void UpdateTrackedShowAutoTrackDownloadFolder(long showId, string? downloadFolder);

    void UpdateTrackedShowAutoTrackReconcileAndLink(long showId, bool autoReconcileAndLink);

    void UpdateTrackedShowAutoTrackTmdbState(
        long showId,
        AutoTrackTmdbState tmdbState,
        string? lastTmdbWeekKey,
        DateTime? lastTmdbRefreshLocal);

    void UpdateTrackedShowAutoTrackScheduleOverrides(
        long showId,
        DayOfWeek? anchorDayOfWeek,
        string? anchorTimeLocal,
        bool clearOverrides);

    void UpdateTrackedShowAutoTrackQualityOverrides(
        long showId,
        string? minQuality,
        int? minSeeders,
        int? minFileSizeMb,
        int? maxFileSizeMb,
        string? allowedQualities,
        bool clearOverrides);

    TrackedShow? GetTrackedShow(long id);

    TrackedShow? GetTrackedShowByTmdbId(int tmdbId);

    long UpsertTrackedShow(TrackedShow show);

    void DeleteTrackedSeasonsAndEpisodes(long showId);

    void DeleteTrackedShow(long showId);

    int DeleteAllTrackedShows();

    void DeleteTrackedMovie(long movieId);

    int DeleteAllTrackedMovies();

    int DeleteFetchJobsForMedia(long mediaId, MediaKind targetKind);

    int DeleteAllFetchJobs();

    void UpsertTrackedSeason(TrackedSeason season);

    IReadOnlyList<TrackedSeason> GetTrackedSeasons(long showId);

    void UpdateTrackedSeasonDownloadFolder(long showId, int seasonNumber, string? downloadFolder);

    void UpdateTrackedSeasonPackMode(long showId, int seasonNumber, SeasonManagementMode mode);

    void UpdateTrackedSeasonHidden(long showId, int seasonNumber, bool isHidden);

    void UpdateTrackedSeasonSelectedPack(long showId, int ownerSeasonNumber, SeasonPackCandidate candidate);

    void ClearTrackedSeasonSelectedPacksForSeasons(long showId, IReadOnlyList<int> seasonNumbers);

    void ClearTrackedSeasonSelectedPack(long showId, int ownerSeasonNumber);

    void UpdateTrackedSeasonPackTorrent(long showId, int ownerSeasonNumber, AddedTorrentResult torrent);

    void UpdateTrackedSeasonLastPackLink(long showId, int ownerSeasonNumber, string torrentHash, DateTime linkedUtc);

    void UpdateTrackedSeasonPackInspection(long showId, int ownerSeasonNumber, PackTorrentInventory inventory);

    void MarkTrackedSeasonPackTorrentRemoved(long showId, int ownerSeasonNumber, string torrentHash);

    void ClearSelectedEpisodeCandidates();

    void ClearSelectedSeasonPackCandidates();

    void ClearSelectedMovieCandidates();

    void UpsertTrackedEpisode(TrackedEpisode episode);

    IReadOnlyList<TrackedEpisode> GetTrackedEpisodes(long showId);

    void UpdateTrackedEpisodeAvailability(long episodeId, EpisodeAvailability availability);

    void UpdateTrackedEpisodeTorrent(
        long episodeId,
        string torrentHash,
        string torrentName,
        string torrentState,
        double torrentProgress);

    void UpdateTrackedEpisodeSelectedCandidate(long episodeId, EpisodeFetchCandidate candidate);

    void ClearTrackedEpisodeSelectedCandidate(long episodeId);

    void UpdateTrackedShowPreferredQuality(long showId, string preferredQuality);

    void UpdateTrackedShowPreferences(long showId, string preferredQuality, string preferredAudioCodec, int minimumSeeders);

    void UpdateTrackedShowRecipe(long showId, string? recipeId);

    void UpdateTrackedShowPackRecipe(long showId, string? packRecipeId);

    void UpdateTrackedShowSeriesStatus(long showId, ShowSeriesStatus seriesStatus);

    void UpdateTrackedShowWatchProgress(long showId, UserWatchStatus watchStatus, int watchedEpisodes);

    void UpdateTrackedShowRating(long showId, double? rating, string? thought);

    void UpdateTrackedShowExcludedAlternativeTitles(long showId, string? excludedAlternativeTitlesJson);

    void UpdateTrackedShowEpisodeOrganization(long showId, string? episodeGroupId, string? episodeGroupName);

    IReadOnlyList<TrackedMovie> GetTrackedMovies();

    TrackedMovie? GetTrackedMovie(long id);

    TrackedMovie? GetTrackedMovieByTmdbId(int tmdbId);

    long UpsertTrackedMovie(TrackedMovie movie);

    void UpdateTrackedMovieAvailability(long movieId, EpisodeAvailability availability);

    void UpdateTrackedMovieWatchStatus(long movieId, UserWatchStatus watchStatus);

    void UpdateTrackedMovieRating(long movieId, double? rating, string? thought);

    void UpdateTrackedMovieTorrent(
        long movieId,
        string torrentHash,
        string torrentName,
        string torrentState,
        double torrentProgress);

    void UpdateTrackedMovieSelectedCandidate(long movieId, EpisodeFetchCandidate candidate);

    void ClearTrackedMovieSelectedCandidate(long movieId);

    void UpdateTrackedMoviePreferences(long movieId, string preferredQuality, string preferredAudioCodec, int minimumSeeders);

    void UpdateTrackedMovieRecipe(long movieId, string? recipeId);

    void UpdateTrackedMovieExcludedAlternativeTitles(long movieId, string? excludedAlternativeTitlesJson);

    IReadOnlyList<TorrentCartOrder> GetTorrentCartOrders(MediaKind? mediaKind = null, long? mediaId = null);

    TorrentCartOrder? GetTorrentCartOrder(long orderId);

    long UpsertTorrentCartOrder(TorrentCartOrder order);

    int DeleteTorrentCartOrders(MediaKind? mediaKind = null, long? mediaId = null);

    int DeleteTorrentCartOrder(long orderId);

    int DeleteTorrentCartOrderCandidates(long orderId);

    IReadOnlyList<TorrentCartOrderCandidate> GetTorrentCartOrderCandidates(long orderId);

    void ReplaceTorrentCartOrderCandidates(long orderId, IReadOnlyList<TorrentCartOrderCandidate> candidates);

    void UpdateTorrentCartOrderCandidateSelection(long orderId, long candidateId);

    void UpdateTorrentCartOrderCandidateAccepted(long orderId, long candidateId, bool isAccepted);

    long CreateFetchJob(FetchJob job);

    IReadOnlyList<FetchJob> GetFetchJobs();

    FetchJob? GetFetchJob(long id);

    void UpdateFetchJob(FetchJob job);

    void DeleteFetchJob(long id);
}
