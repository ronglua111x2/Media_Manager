using media_management_app.Models;

namespace media_management_app.Services;

public interface ITrackedShowService
{
    Task<IReadOnlyList<TmdbShowSearchResult>> SearchShowsAsync(string query, CancellationToken cancellationToken = default);

    Task<TrackedShow> AddShowAsync(TmdbShowSearchResult result, CancellationToken cancellationToken = default);

    Task<TrackedShow> RefreshShowAsync(TrackedShow show, CancellationToken cancellationToken = default);

    IReadOnlyList<TrackedShow> GetShows();

    IReadOnlyList<TrackedEpisode> GetEpisodes(long showId);

    IReadOnlyList<TrackedSeason> GetSeasons(long showId);

    void RefreshAvailability();

    void RefreshAvailability(long showId);

    void UpdateWanted(long episodeId, bool isWanted);

    void UpdateSeasonDownloadFolder(long showId, int seasonNumber, string? downloadFolder);

    void UpdateTorrentState(long episodeId, AddedTorrentResult torrent);

    void MarkTorrentRemoved(long episodeId, string torrentHash);

    void UpdateSelectedCandidate(long episodeId, EpisodeFetchCandidate candidate);

    void UpdatePreferredQuality(long showId, string preferredQuality);

    void UpdatePreferences(long showId, string preferredQuality, string preferredAudioCodec, int minimumSeeders);
}
