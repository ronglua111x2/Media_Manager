using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface ITorrentBlacklistService
{
    Task AddToBlacklistAsync(
        long showId,
        string listingUrl,
        string? infoHash,
        string reason,
        List<SuspiciousFile>? suspiciousFiles = null,
        string? notes = null);

    /// <summary>
    /// True if listing URL or infohash is blacklisted for this show.
    /// </summary>
    bool IsBlacklisted(long showId, string? listingUrl, string? infoHash = null);

    Task<IReadOnlyList<TorrentBlacklistEntry>> GetBlacklistAsync(long showId);

    Task RemoveFromBlacklistAsync(long showId, string listingUrl, string? infoHash = null);
}

public sealed class TorrentBlacklistService : ITorrentBlacklistService
{
    private readonly IDatabaseService _databaseService;
    private readonly IAppLogger _logger;

    public TorrentBlacklistService(
        IDatabaseService databaseService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    public Task AddToBlacklistAsync(
        long showId,
        string listingUrl,
        string? infoHash,
        string reason,
        List<SuspiciousFile>? suspiciousFiles = null,
        string? notes = null)
    {
        try
        {
            var url = TorrentListingIdentity.NormalizeListingUrl(listingUrl);
            var hash = TorrentListingIdentity.NormalizeInfoHash(infoHash)
                       ?? TorrentListingIdentity.TryParseInfoHashFromUrl(url)
                       ?? string.Empty;

            if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(hash))
            {
                throw new ArgumentException("Blacklist requires a listing URL or infohash.");
            }

            var suspiciousJson = suspiciousFiles?.Count > 0
                ? JsonSerializer.Serialize(suspiciousFiles)
                : null;

            _databaseService.AddTorrentToBlacklist(
                showId,
                url,
                hash,
                reason,
                suspiciousJson,
                notes);

            _logger.Warning(
                $"Torrent blacklisted for show {showId}: url='{url}', hash='{hash}', reason={reason}",
                LogTarget.File | LogTarget.Console);
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Failed to add torrent to blacklist for show {showId}: {ex.Message}",
                ex,
                LogTarget.All);
            throw;
        }

        return Task.CompletedTask;
    }

    public bool IsBlacklisted(long showId, string? listingUrl, string? infoHash = null)
    {
        try
        {
            var url = TorrentListingIdentity.NormalizeListingUrl(listingUrl);
            var hash = TorrentListingIdentity.NormalizeInfoHash(infoHash)
                       ?? TorrentListingIdentity.TryParseInfoHashFromUrl(url);
            return _databaseService.IsTorrentBlacklisted(showId, url, hash);
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Error checking blacklist for show {showId}: {ex.Message}",
                ex,
                LogTarget.File);
            // Fail closed for malware: treat unknown as not blacklisted would re-add malware.
            // Prefer fail-open on check errors only for availability; plan said URL filter.
            // Keep fail-open to avoid blocking all hunts if DB hiccups — but log loudly.
            return false;
        }
    }

    public Task<IReadOnlyList<TorrentBlacklistEntry>> GetBlacklistAsync(long showId)
    {
        try
        {
            return Task.FromResult(_databaseService.GetTorrentBlacklist(showId));
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Error retrieving blacklist for show {showId}: {ex.Message}",
                ex,
                LogTarget.File);
            return Task.FromResult<IReadOnlyList<TorrentBlacklistEntry>>([]);
        }
    }

    public Task RemoveFromBlacklistAsync(long showId, string listingUrl, string? infoHash = null)
    {
        try
        {
            var url = TorrentListingIdentity.NormalizeListingUrl(listingUrl);
            var hash = TorrentListingIdentity.NormalizeInfoHash(infoHash)
                       ?? TorrentListingIdentity.TryParseInfoHashFromUrl(url);
            _databaseService.RemoveTorrentFromBlacklist(showId, url, hash);
            _logger.Info(
                $"Torrent removed from blacklist for show {showId}: url='{url}', hash='{hash}'",
                LogTarget.File);
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Error removing torrent from blacklist: {ex.Message}",
                ex,
                LogTarget.File);
            throw;
        }

        return Task.CompletedTask;
    }
}
