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
    /// Returns true when the torrent is verified gone.
    /// Throws <see cref="TorrentCleanupFailedException"/> when deletion cannot be confirmed.
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
    public const int MaxDeleteAttempts = 3;

    private static readonly TimeSpan AttemptDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(20);

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
        var reasonText = string.IsNullOrWhiteSpace(reason) ? "cleanup" : reason.Trim();
        _logger.Warning(
            $"Deleting torrent {torrentHash} from qBittorrent (reason={reasonText}, deleteFiles={deleteFiles})",
            LogTarget.File | LogTarget.Console);

        using var timeoutCts = new CancellationTokenSource(CleanupTimeout);
        var cleanupToken = timeoutCts.Token;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxDeleteAttempts; attempt++)
        {
            try
            {
                await _qbittorrentClient.DeleteTorrentsAsync(
                    new[] { torrentHash },
                    deleteFiles: deleteFiles,
                    cancellationToken: cleanupToken);

                if (await IsTorrentDeletedAsync(torrentHash, cleanupToken))
                {
                    _logger.Info(
                        $"Torrent {torrentHash} deleted from qBittorrent (reason={reasonText}).",
                        LogTarget.File | LogTarget.Console);
                    return true;
                }

                lastError = null;
                _logger.Warning(
                    $"Delete attempt {attempt}/{MaxDeleteAttempts} did not remove torrent {torrentHash} (reason={reasonText}).",
                    LogTarget.File | LogTarget.Console);
            }
            catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
            {
                lastError = ex;
                _logger.Warning(
                    $"Cleanup timed out while deleting torrent {torrentHash} (reason={reasonText}).",
                    LogTarget.File | LogTarget.Console);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                _logger.Warning(
                    $"Delete attempt {attempt}/{MaxDeleteAttempts} failed for torrent {torrentHash}: {ex.Message}",
                    LogTarget.File | LogTarget.Console);
            }

            if (attempt < MaxDeleteAttempts && !timeoutCts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(AttemptDelay, cleanupToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        await TryPauseFailSafeAsync(torrentHash);
        throw new TorrentCleanupFailedException(torrentHash, reasonText, lastError);
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

    private async Task TryPauseFailSafeAsync(string torrentHash)
    {
        try
        {
            await _qbittorrentClient.PauseTorrentsAsync(new[] { torrentHash });
            _logger.Warning(
                $"Paused torrent {torrentHash} after unverified cleanup. Inspect qBittorrent.",
                LogTarget.File | LogTarget.Console);
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Failed to pause torrent {torrentHash} after unverified cleanup: {ex.Message}",
                ex,
                LogTarget.File | LogTarget.Console);
        }
    }
}
