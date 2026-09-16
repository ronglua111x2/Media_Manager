using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface ITorrentAddGateService
{
    /// <summary>
    /// Returns the live qBittorrent torrent whose infohash matches a magnet listing URL, or null.
    /// HTTP listings without a magnet infohash cannot be matched.
    /// </summary>
    Task<AddedTorrentResult?> TryGetExistingByListingUrlAsync(
        string? listingUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the selected candidate while running, waits for the file list, validates malware/payload,
    /// then returns. When <paramref name="existingTorrent"/> is set, skips qBittorrent add and
    /// validates that copy instead. Throws <see cref="MaliciousTorrentException"/> after verified
    /// delete+blacklist. Throws <see cref="TorrentCleanupFailedException"/> when a rejected torrent
    /// cannot be verified deleted. Empty file list deletes without blacklisting. When content
    /// validation is disabled, returns immediately after add (plus infohash blacklist check).
    /// </summary>
    Task<AddedTorrentResult> AddPausedValidateAndResumeAsync(
        TorrentCartOrder order,
        string savePath,
        CancellationToken cancellationToken = default,
        AddedTorrentResult? existingTorrent = null);
}

public sealed class TorrentAddGateService : ITorrentAddGateService
{
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly ISettingsService _settingsService;
    private readonly ITorrentContentValidationService _contentValidationService;
    private readonly ITorrentCleanupService _cleanupService;
    private readonly ITorrentBlacklistService _blacklistService;
    private readonly IAppLogger _logger;

    public TorrentAddGateService(
        IQbittorrentClient qbittorrentClient,
        ISettingsService settingsService,
        ITorrentContentValidationService contentValidationService,
        ITorrentCleanupService cleanupService,
        ITorrentBlacklistService blacklistService,
        IAppLogger logger)
    {
        _qbittorrentClient = qbittorrentClient;
        _settingsService = settingsService;
        _contentValidationService = contentValidationService;
        _cleanupService = cleanupService;
        _blacklistService = blacklistService;
        _logger = logger;
    }

    public async Task<AddedTorrentResult?> TryGetExistingByListingUrlAsync(
        string? listingUrl,
        CancellationToken cancellationToken = default)
    {
        var infoHash = TorrentListingIdentity.TryParseInfoHashFromUrl(listingUrl);
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        var torrents = await _qbittorrentClient.GetTorrentsAsync(cancellationToken);
        return torrents.FirstOrDefault(torrent =>
            string.Equals(torrent.Hash, infoHash, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AddedTorrentResult> AddPausedValidateAndResumeAsync(
        TorrentCartOrder order,
        string savePath,
        CancellationToken cancellationToken = default,
        AddedTorrentResult? existingTorrent = null)
    {
        if (string.IsNullOrWhiteSpace(order.SelectedCandidateUrl))
        {
            throw new InvalidOperationException("Order is missing a selected candidate URL.");
        }

        if (_blacklistService.IsBlacklisted(order.MediaId, order.SelectedCandidateUrl))
        {
            throw new MaliciousTorrentException("Selected candidate is blacklisted for this show.");
        }

        var category = _settingsService.Current.AutoTorrent.GetCategoryFor(order.TargetKind);
        const string managedTag = "media-manager";
        AddedTorrentResult addedTorrent;
        if (existingTorrent is not null && !string.IsNullOrWhiteSpace(existingTorrent.Hash))
        {
            _logger.Info(
                $"Using existing qBittorrent download '{existingTorrent.Name}' for '{order.Title}' (hash={existingTorrent.Hash}).",
                LogTarget.File | LogTarget.Console);
            await _qbittorrentClient.ApplyManagedTorrentSettingsAsync(
                existingTorrent.Hash,
                savePath,
                category,
                managedTag,
                cancellationToken);
            addedTorrent = existingTorrent;
        }
        else
        {
            _logger.Info(
                $"Add with validation: '{order.Title}' candidate '{order.SelectedCandidateName}'.",
                LogTarget.File | LogTarget.Console);

            addedTorrent = await _qbittorrentClient.AddTorrentAsync(
                new AddTorrentRequest
                {
                    Url = order.SelectedCandidateUrl,
                    PluginName = order.SelectedCandidatePlugin,
                    SavePath = savePath,
                    Category = category,
                    Tags = managedTag,
                    Paused = false
                },
                cancellationToken);
        }

        _logger.Debug(
            $"Torrent added for '{order.Title}'. Hash={addedTorrent.Hash}, Name='{addedTorrent.Name}'.",
            LogTarget.File);

        if (_blacklistService.IsBlacklisted(order.MediaId, order.SelectedCandidateUrl, addedTorrent.Hash))
        {
            _logger.Warning(
                $"Skipped add — infohash already blacklisted for '{order.Title}' (hash={addedTorrent.Hash}).",
                LogTarget.File | LogTarget.Console);

            await DeleteRejectedTorrentAsync(
                order,
                addedTorrent.Hash,
                cleanupReason: "blacklisted infohash",
                blacklistReason: "Blacklisted infohash",
                notes: $"Listing URL resolved to known-bad infohash '{addedTorrent.Hash}'",
                rejectMessage: "Listing matches a blacklisted infohash.",
                cancellationToken);
        }

        var validationEnabled = _settingsService.Current.TorrentValidation?.EnableContentValidation ?? true;
        if (!validationEnabled)
        {
            _logger.Info(
                $"Content validation skipped for '{order.Title}' (hash={addedTorrent.Hash}).",
                LogTarget.File | LogTarget.Console);
            return await GetLiveTorrentAsync(addedTorrent, cancellationToken);
        }

        var files = await WaitForTorrentFilesAsync(addedTorrent.Hash, cancellationToken);
        if (files.Count == 0)
        {
            _logger.Warning(
                $"Metadata timeout for '{order.Title}' (hash={addedTorrent.Hash}): empty file list.",
                LogTarget.File | LogTarget.Console);
            await DeleteRejectedTorrentAsync(
                order,
                addedTorrent.Hash,
                cleanupReason: "metadata timeout",
                blacklistReason: null,
                notes: null,
                rejectMessage: $"Torrent metadata timed out with empty file list (hash={addedTorrent.Hash}).",
                cancellationToken,
                maliciousReject: false);
        }

        _logger.Info(
            $"Torrent file list ready for '{order.Title}' hash={addedTorrent.Hash}: {files.Count} file(s).",
            LogTarget.File | LogTarget.Console);

        var isPack = order.EpisodeId is null && order.TargetKind != MediaKind.Movie;
        var listingName = FirstNonEmpty(addedTorrent.Name, order.SelectedCandidateName, order.Title);
        var validation = await _contentValidationService.ValidateFilesAsync(
            addedTorrent.Hash,
            files,
            cancellationToken,
            listingName,
            isPack);

        if (!validation.IsValid && validation.Recommendation == TorrentHandleRecommendation.Delete)
        {
            _logger.Warning(
                $"Malware delete for '{order.Title}' (hash={addedTorrent.Hash}): {validation.Summary}",
                LogTarget.File | LogTarget.Console);
            foreach (var suspicious in validation.SuspiciousFiles)
            {
                _logger.Debug(
                    $"Malware file '{suspicious.FileName}' ext={suspicious.Extension} level={suspicious.SuspicionLevel}: {suspicious.Reason}",
                    LogTarget.File);
            }

            await DeleteRejectedTorrentAsync(
                order,
                addedTorrent.Hash,
                cleanupReason: $"malware: {validation.Summary}",
                blacklistReason: $"Malware: {validation.Summary}",
                notes: $"Rejected candidate '{order.SelectedCandidateName}'",
                rejectMessage: validation.Summary,
                cancellationToken,
                suspiciousFiles: validation.SuspiciousFiles);
        }

        _logger.Info(
            $"Validation passed for '{order.Title}' — download continues (hash={addedTorrent.Hash}).",
            LogTarget.File | LogTarget.Console);

        return await GetLiveTorrentAsync(addedTorrent, cancellationToken);
    }

    private async Task DeleteRejectedTorrentAsync(
        TorrentCartOrder order,
        string torrentHash,
        string cleanupReason,
        string? blacklistReason,
        string? notes,
        string rejectMessage,
        CancellationToken cancellationToken,
        bool maliciousReject = true,
        List<SuspiciousFile>? suspiciousFiles = null)
    {
        try
        {
            await _cleanupService.DeleteTorrentAsync(
                torrentHash,
                deleteFiles: true,
                reason: cleanupReason,
                cancellationToken);
        }
        catch (TorrentCleanupFailedException)
        {
            if (!string.IsNullOrWhiteSpace(blacklistReason))
            {
                await TryBlacklistAsync(
                    order,
                    torrentHash,
                    blacklistReason,
                    notes,
                    suspiciousFiles);
            }

            throw;
        }

        if (!string.IsNullOrWhiteSpace(blacklistReason))
        {
            await TryBlacklistAsync(
                order,
                torrentHash,
                blacklistReason,
                notes,
                suspiciousFiles);
        }

        if (maliciousReject)
        {
            throw new MaliciousTorrentException(rejectMessage);
        }

        throw new InvalidOperationException(rejectMessage);
    }

    private async Task TryBlacklistAsync(
        TorrentCartOrder order,
        string torrentHash,
        string blacklistReason,
        string? notes,
        List<SuspiciousFile>? suspiciousFiles)
    {
        try
        {
            await _blacklistService.AddToBlacklistAsync(
                order.MediaId,
                order.SelectedCandidateUrl,
                torrentHash,
                blacklistReason,
                suspiciousFiles,
                notes);
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Failed to blacklist torrent for '{order.Title}' (hash={torrentHash}): {ex.Message}",
                ex,
                LogTarget.File | LogTarget.Console);
        }
    }

    private async Task<IReadOnlyList<TorrentContentFile>> WaitForTorrentFilesAsync(
        string torrentHash,
        CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Clamp(
            _settingsService.Current.TorrentValidation?.ValidationTimeoutSeconds ?? 90,
            5,
            120);
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = await _qbittorrentClient.GetTorrentFilesAsync(torrentHash, cancellationToken);
            if (files.Count > 0)
            {
                return files;
            }

            await Task.Delay(1000, cancellationToken);
        }

        return await _qbittorrentClient.GetTorrentFilesAsync(torrentHash, cancellationToken);
    }

    private async Task<AddedTorrentResult> GetLiveTorrentAsync(
        AddedTorrentResult addedTorrent,
        CancellationToken cancellationToken)
    {
        var live = (await _qbittorrentClient.GetTorrentsAsync(cancellationToken))
            .FirstOrDefault(torrent => string.Equals(torrent.Hash, addedTorrent.Hash, StringComparison.OrdinalIgnoreCase));
        return live ?? addedTorrent;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}
