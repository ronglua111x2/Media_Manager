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

    Task<IReadOnlyList<AddedTorrentResult>> GetTorrentsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TorrentContentFile>> GetTorrentFilesAsync(string hash, CancellationToken cancellationToken = default);

    Task DeleteTorrentsAsync(IEnumerable<string> hashes, bool deleteFiles = false, CancellationToken cancellationToken = default);

    Task PauseTorrentsAsync(IEnumerable<string> hashes, CancellationToken cancellationToken = default);

    Task ResumeTorrentsAsync(IEnumerable<string> hashes, CancellationToken cancellationToken = default);

    Task<TorrentMetadataProbeResult> ProbeTorrentMetadataAsync(TorrentSearchResult result, CancellationToken cancellationToken = default);
}
