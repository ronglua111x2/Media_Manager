using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface ITorrentAddGateService
{
    /// <summary>
    /// Adds the selected candidate while running, waits for the file list, validates malware/payload,
    /// then returns. Throws <see cref="MaliciousTorrentException"/> after delete+blacklist.
    /// Empty file list deletes without blacklisting. When content validation is disabled,
    /// returns immediately after add (plus infohash blacklist check).
    /// </summary>
    Task<AddedTorrentResult> AddPausedValidateAndResumeAsync(
        TorrentCartOrder order,
        string savePath,
        CancellationToken cancellationToken = default);
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

    public async Task<AddedTorrentResult> AddPausedValidateAndResumeAsync(
        TorrentCartOrder order,
        string savePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(order.SelectedCandidateUrl))
        {
            throw new InvalidOperationException("Order is missing a selected candidate URL.");
        }

        if (_blacklistService.IsBlacklisted(order.MediaId, order.SelectedCandidateUrl))
        {
            throw new MaliciousTorrentException("Selected candidate is blacklisted for this show.");
        }

        _logger.Info(
            $"Add with validation: '{order.Title}' candidate '{order.SelectedCandidateName}'.",
            LogTarget.File | LogTarget.Console);

        var addedTorrent = await _qbittorrentClient.AddTorrentAsync(
            new AddTorrentRequest
            {
                Url = order.SelectedCandidateUrl,
                PluginName = order.SelectedCandidatePlugin,
                SavePath = savePath,
                Category = _settingsService.Current.AutoTorrent.GetCategoryFor(order.TargetKind),
                Tags = "media-manager",
                Paused = false
            },
            cancellationToken);

        _logger.Debug(
            $"Torrent added for '{order.Title}'. Hash={addedTorrent.Hash}, Name='{addedTorrent.Name}'.",
            LogTarget.File);

        if (_blacklistService.IsBlacklisted(order.MediaId, order.SelectedCandidateUrl, addedTorrent.Hash))
        {
            _logger.Warning(
                $"Skipped add — infohash already blacklisted for '{order.Title}' (hash={addedTorrent.Hash}).",
                LogTarget.File | LogTarget.Console);

            await _cleanupService.DeleteTorrentAsync(
                addedTorrent.Hash,
                deleteFiles: true,
                reason: "blacklisted infohash",
                cancellationToken);

            try
            {
                await _blacklistService.AddToBlacklistAsync(
                    order.MediaId,
                    order.SelectedCandidateUrl,
                    addedTorrent.Hash,
                    "Blacklisted infohash",
                    notes: $"Listing URL resolved to known-bad infohash '{addedTorrent.Hash}'");
            }
            catch (Exception ex)
            {
                _logger.Error(
                    $"Failed to blacklist listing URL after known infohash skip for '{order.Title}' (hash={addedTorrent.Hash}): {ex.Message}",
                    ex,
                    LogTarget.File | LogTarget.Console);
            }

            throw new MaliciousTorrentException("Listing matches a blacklisted infohash.");
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
            await _cleanupService.DeleteTorrentAsync(
                addedTorrent.Hash,
                deleteFiles: true,
                reason: "metadata timeout",
                cancellationToken);
            throw new InvalidOperationException(
                $"Torrent metadata timed out with empty file list (hash={addedTorrent.Hash}).");
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

            await _cleanupService.DeleteTorrentAsync(
                addedTorrent.Hash,
                deleteFiles: true,
                reason: $"malware: {validation.Summary}",
                cancellationToken);

            try
            {
                await _blacklistService.AddToBlacklistAsync(
                    order.MediaId,
                    order.SelectedCandidateUrl,
                    addedTorrent.Hash,
                    $"Malware: {validation.Summary}",
                    validation.SuspiciousFiles,
                    $"Rejected candidate '{order.SelectedCandidateName}'");
            }
            catch (Exception ex)
            {
                _logger.Error(
                    $"Failed to blacklist malware torrent for '{order.Title}' after delete (hash={addedTorrent.Hash}): {ex.Message}",
                    ex,
                    LogTarget.File | LogTarget.Console);
            }

            throw new MaliciousTorrentException(validation.Summary);
        }

        _logger.Info(
            $"Validation passed for '{order.Title}' — download continues (hash={addedTorrent.Hash}).",
            LogTarget.File | LogTarget.Console);

        return await GetLiveTorrentAsync(addedTorrent, cancellationToken);
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
