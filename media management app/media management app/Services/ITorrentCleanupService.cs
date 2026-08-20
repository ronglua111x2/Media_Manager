using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Service for cleaning up malicious torrents from qBittorrent.
/// </summary>
public interface ITorrentCleanupService
{
    /// <summary>
    /// Delete torrent from qBittorrent and remove files from disk.
    /// </summary>
    Task<bool> DeleteTorrentAsync(
        string torrentHash,
        bool deleteFiles = true,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify torrent was successfully deleted.
    /// </summary>
    Task<bool> IsTorrentDeletedAsync(
        string torrentHash,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles cleanup and removal of malicious torrents from qBittorrent.
/// </summary>
public sealed class TorrentCleanupService : ITorrentCleanupService
{
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IAppLogger _logger;

    public TorrentCleanupService(
        IQbittorrentClient qbittorrentClient,
        IAppLogger logger)
    {
        _qbittorrentClient = qbittorrentClient;
        _logger = logger;
    }

    public async Task<bool> DeleteTorrentAsync(
        string torrentHash,
        bool deleteFiles = true,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var reasonText = string.IsNullOrWhiteSpace(reason) ? "cleanup" : reason.Trim();
            _logger.Warning(
                $"Deleting torrent {torrentHash} from qBittorrent (reason={reasonText}, deleteFiles={deleteFiles})",
                LogTarget.File | LogTarget.Console);

            await _qbittorrentClient.DeleteTorrentsAsync(
                new[] { torrentHash },
                deleteFiles: deleteFiles,
                cancellationToken: cancellationToken);

            var deleted = await IsTorrentDeletedAsync(torrentHash, cancellationToken);
            if (deleted)
            {
                _logger.Info(
                    $"Torrent {torrentHash} deleted from qBittorrent (reason={reasonText}).",
                    LogTarget.File | LogTarget.Console);
                return true;
            }

            _logger.Warning(
                $"Failed to verify deletion of torrent {torrentHash} (reason={reasonText}).",
                LogTarget.File | LogTarget.Console);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Error deleting torrent {torrentHash}: {ex.Message}",
                ex,
                LogTarget.All);
            return false;
        }
    }

    public async Task<bool> IsTorrentDeletedAsync(
        string torrentHash,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var torrents = await _qbittorrentClient.GetTorrentsAsync(cancellationToken);
            var stillExists = torrents.Any(t => 
                string.Equals(t.Hash, torrentHash, StringComparison.OrdinalIgnoreCase));
            return !stillExists;
        }
        catch
        {
            return false;
        }
    }
}
