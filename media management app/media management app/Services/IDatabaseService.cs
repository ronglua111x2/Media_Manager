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

    TrackedShow? GetTrackedShow(long id);

    TrackedShow? GetTrackedShowByTmdbId(int tmdbId);

    long UpsertTrackedShow(TrackedShow show);

    void DeleteTrackedSeasonsAndEpisodes(long showId);

    void UpsertTrackedSeason(TrackedSeason season);

    IReadOnlyList<TrackedSeason> GetTrackedSeasons(long showId);

    void UpdateTrackedSeasonDownloadFolder(long showId, int seasonNumber, string? downloadFolder);

    void UpdateTrackedSeasonPackMode(long showId, int seasonNumber, SeasonManagementMode mode);

    void UpdateTrackedSeasonSelectedPack(long showId, int ownerSeasonNumber, SeasonPackCandidate candidate);

    void ClearTrackedSeasonSelectedPacksForSeasons(long showId, IReadOnlyList<int> seasonNumbers);

    void ClearTrackedSeasonSelectedPack(long showId, int ownerSeasonNumber);

    void UpdateTrackedSeasonPackTorrent(long showId, int ownerSeasonNumber, AddedTorrentResult torrent);

    void MarkTrackedSeasonPackTorrentRemoved(long showId, int ownerSeasonNumber, string torrentHash);

    void ClearSelectedEpisodeCandidates();

    void ClearSelectedSeasonPackCandidates();

    void ClearSelectedMovieCandidates();

    void UpsertTrackedEpisode(TrackedEpisode episode);

    IReadOnlyList<TrackedEpisode> GetTrackedEpisodes(long showId);

    void UpdateTrackedEpisodeWanted(long episodeId, bool isWanted);

    void UpdateTrackedEpisodeAvailability(long episodeId, EpisodeAvailability availability);

    void UpdateTrackedEpisodeTorrent(
        long episodeId,
        string torrentHash,
        string torrentName,
        string torrentState,
        double torrentProgress);

    void UpdateTrackedEpisodeSelectedCandidate(long episodeId, EpisodeFetchCandidate candidate);

    void UpdateTrackedShowPreferredQuality(long showId, string preferredQuality);

    void UpdateTrackedShowPreferences(long showId, string preferredQuality, string preferredAudioCodec, int minimumSeeders);

    void UpdateTrackedShowRecipe(long showId, string? recipeId);

    IReadOnlyList<TrackedMovie> GetTrackedMovies();

    TrackedMovie? GetTrackedMovie(long id);

    TrackedMovie? GetTrackedMovieByTmdbId(int tmdbId);

    long UpsertTrackedMovie(TrackedMovie movie);

    void UpdateTrackedMovieWanted(long movieId, bool isWanted);

    void UpdateTrackedMovieAvailability(long movieId, EpisodeAvailability availability);

    void UpdateTrackedMovieTorrent(
        long movieId,
        string torrentHash,
        string torrentName,
        string torrentState,
        double torrentProgress);

    void UpdateTrackedMovieSelectedCandidate(long movieId, EpisodeFetchCandidate candidate);

    void UpdateTrackedMoviePreferences(long movieId, string preferredQuality, string preferredAudioCodec, int minimumSeeders);

    void UpdateTrackedMovieRecipe(long movieId, string? recipeId);

    long CreateFetchJob(FetchJob job);

    IReadOnlyList<FetchJob> GetFetchJobs();

    FetchJob? GetFetchJob(long id);

    void UpdateFetchJob(FetchJob job);

    void DeleteFetchJob(long id);
}
