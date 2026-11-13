using media_management_app.Models;

namespace media_management_app.Services;

public interface IQbittorrentClient
{
    Task<string> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight WebUI probe that distinguishes unreachable (bind-fail / timeout)
    /// from auth failure and invalid URL. Does not restart the process.
    /// </summary>
    Task<QbittorrentWebUiProbeResult> ProbeWebUiAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(TorrentSearchRequest request, CancellationToken cancellationToken = default);

    Task<int> StartSearchAsync(TorrentSearchRequest request, CancellationToken cancellationToken = default);

    Task<SearchJobResults> GetSearchResultsAsync(int searchId, int limit, int offset = 0, CancellationToken cancellationToken = default);

    Task StopSearchAsync(int searchId, CancellationToken cancellationToken = default);

    Task DeleteSearchAsync(int searchId, CancellationToken cancellationToken = default);

    Task<AddedTorrentResult> AddTorrentAsync(AddTorrentRequest request, CancellationToken cancellationToken = default);

    Task ApplyManagedTorrentSettingsAsync(
        string hash,
        string? savePath,
        string? category,
        string tags,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AddedTorrentResult>> GetTorrentsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TorrentContentFile>> GetTorrentFilesAsync(string hash, CancellationToken cancellationToken = default);

    Task DeleteTorrentsAsync(IEnumerable<string> hashes, bool deleteFiles = false, CancellationToken cancellationToken = default);

    Task PauseTorrentsAsync(IEnumerable<string> hashes, CancellationToken cancellationToken = default);

    Task ResumeTorrentsAsync(IEnumerable<string> hashes, CancellationToken cancellationToken = default);

    /// <summary><c>app/webapiVersion</c>; 0.0 when it cannot be read (no optional add feature is used).</summary>
    Task<Version> GetWebApiVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// S0: reads the file list of <paramref name="url"/> without adding it. A .torrent served over HTTP(S) is downloaded
    /// and parsed by the app; a magnet (also one found on a details page) goes through <c>torrents/fetchMetadata</c>
    /// when <paramref name="useFetchMetadata"/>. Returns null when the list cannot be read before adding.
    /// </summary>
    Task<PreAddInspection?> InspectBeforeAddAsync(
        string url,
        bool useFetchMetadata,
        TimeSpan metadataTimeout,
        CancellationToken cancellationToken = default);

    /// <summary><c>torrents/filePrio</c> for the given file indexes (0 = do not download).</summary>
    Task SetFilePrioritiesAsync(
        string hash,
        IReadOnlyCollection<int> fileIndexes,
        int priority,
        CancellationToken cancellationToken = default);

    Task<TorrentMetadataProbeResult> ProbeTorrentMetadataAsync(TorrentSearchResult result, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchPluginInfo>> GetSearchPluginsAsync(CancellationToken cancellationToken = default);
}
