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

    /// <summary>Returns the live qBittorrent torrent with this infohash, or null.</summary>
    Task<AddedTorrentResult?> TryGetExistingByHashAsync(
        string? infoHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the selected candidate so that nothing downloads before its file list passed the malware/payload check
    /// (S0): the list is read before the add when possible (.torrent bytes, or fetchMetadata on WebAPI &gt;= 2.11.9);
    /// otherwise the torrent is added with stopCondition=MetadataReceived (4.5+) and started after the check. Files
    /// outside the download allow-list get priority 0. An add that ends early stops the torrent it added. When <paramref name="existingTorrent"/> is set, skips qBittorrent add and
    /// validates that copy instead. Throws <see cref="MaliciousTorrentException"/> after verified
    /// delete+blacklist. Throws <see cref="TorrentCleanupFailedException"/> when a rejected torrent
    /// cannot be verified deleted. Empty file list deletes without blacklisting. When content
    /// validation is disabled, returns immediately after add (plus infohash blacklist check).
    /// <paramref name="onTorrentAdded"/> runs once the torrent is in qBittorrent and its hash is known,
    /// before validation, so callers can persist the hash in case the app dies mid-validation.
    /// <paramref name="isResetHash"/> (<see cref="ResetTorrentGuard.For"/>): a torrent the order's target was reset
    /// from is never attached: refused before adding (magnet hash, existing copy, left untouched) or, when its hash is
    /// only known after adding, deleted again; throws <see cref="ResetTorrentReusedException"/>.
    /// </summary>
    Task<AddedTorrentResult> AddPausedValidateAndResumeAsync(
        TorrentCartOrder order,
        string savePath,
        CancellationToken cancellationToken = default,
        AddedTorrentResult? existingTorrent = null,
        Action<AddedTorrentResult>? onTorrentAdded = null,
        Func<string?, bool>? isResetHash = null);
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
        return await TryGetExistingByHashAsync(
            TorrentListingIdentity.TryParseInfoHashFromUrl(listingUrl),
            cancellationToken);
    }

    public async Task<AddedTorrentResult?> TryGetExistingByHashAsync(
        string? infoHash,
        CancellationToken cancellationToken = default)
    {
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
        AddedTorrentResult? existingTorrent = null,
        Action<AddedTorrentResult>? onTorrentAdded = null,
        Func<string?, bool>? isResetHash = null)
    {
        if (string.IsNullOrWhiteSpace(order.SelectedCandidateUrl))
        {
            throw new InvalidOperationException("Order is missing a selected candidate URL.");
        }

        if (_blacklistService.IsBlacklisted(order.MediaId, order.SelectedCandidateUrl))
        {
            throw new MaliciousTorrentException("Selected candidate is blacklisted for this show.");
        }

        // The torrent this target was reset from (cross-review 2, B): refuse before touching qBittorrent.
        if (isResetHash is not null &&
            (isResetHash(TorrentListingIdentity.TryParseInfoHashFromUrl(order.SelectedCandidateUrl)) ||
             isResetHash(existingTorrent?.Hash)))
        {
            throw new ResetTorrentReusedException($"'{order.Title}' was reset for redownload from this torrent; choose another candidate.");
        }

        var category = _settingsService.Current.AutoTorrent.GetCategoryFor(order.TargetKind);
        const string managedTag = "media-manager";
        var validationConfig = _settingsService.Current.TorrentValidation ?? new TorrentValidationConfig();
        var validationEnabled = validationConfig.EnableContentValidation;
        var isPack = order.EpisodeId is null && order.TargetKind != MediaKind.Movie;

        AddedTorrentResult addedTorrent;
        string? addedByGateHash = null;
        var startAfterValidation = false;
        var finished = false;
        try
        {
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

                // S0 (SEC-A03): read the file list before the torrent can download anything. A .torrent is downloaded
                // and parsed by the app; a magnet goes through qBittorrent's fetchMetadata (WebAPI >= 2.11.9).
                var webApiVersion = validationEnabled
                    ? await _qbittorrentClient.GetWebApiVersionAsync(cancellationToken)
                    : new Version(0, 0);
                PreAddInspection? inspection = null;
                if (validationEnabled)
                {
                    inspection = await _qbittorrentClient.InspectBeforeAddAsync(
                        order.SelectedCandidateUrl,
                        QbittorrentWebApiCompatibility.SupportsFetchMetadata(webApiVersion),
                        TimeSpan.FromSeconds(MetadataTimeoutSeconds()),
                        cancellationToken);
                    if (inspection is not null)
                    {
                        await CheckBeforeAddAsync(order, inspection, isPack, isResetHash, cancellationToken);
                    }
                }

                var prefetched = inspection is { MetadataPrefetched: true };
                var request = new AddTorrentRequest
                {
                    Url = inspection?.AddUrl ?? order.SelectedCandidateUrl,
                    PluginName = inspection is null ? order.SelectedCandidatePlugin : string.Empty,
                    SavePath = savePath,
                    Category = category,
                    Tags = managedTag,
                    TorrentBytes = inspection?.TorrentBytes,
                    TorrentFileName = inspection?.TorrentFileName ?? "download.torrent",
                    // A checked .torrent has its metadata: add it stopped on every qBittorrent version.
                    Paused = inspection?.TorrentBytes is not null,
                    // A magnet whose metadata qBittorrent fetched starts at once, extras already at priority 0.
                    FilePriorities = prefetched
                        ? inspection!.Files.Select(file => TorrentDownloadAllowList.IsWanted(file.Name, validationConfig) ? 1 : 0).ToList()
                        : null,
                    // Not checked before the add: let qBittorrent stop once it has the file list (4.5+).
                    StopAfterMetadata = validationEnabled &&
                                        inspection is null &&
                                        QbittorrentWebApiCompatibility.SupportsStopCondition(webApiVersion)
                };

                addedTorrent = await _qbittorrentClient.AddTorrentAsync(request, cancellationToken);
                addedByGateHash = addedTorrent.Hash;
                startAfterValidation = request.Paused || request.StopAfterMetadata;
            }

            _logger.Debug(
                $"Torrent added for '{order.Title}'. Hash={addedTorrent.Hash}, Name='{addedTorrent.Name}'.",
                LogTarget.File);

            if (existingTorrent is null && isResetHash is not null && isResetHash(addedTorrent.Hash))
            {
                // Only known after the add (a .torrent link): this is a fresh copy of the rejected download, remove it.
                _logger.Warning(
                    $"Added torrent for '{order.Title}' is the one it was reset from (hash={addedTorrent.Hash}); removing it.",
                    LogTarget.File | LogTarget.Console);
                finished = true;
                await _cleanupService.DeleteTorrentAsync(addedTorrent.Hash, deleteFiles: true, reason: "reset for redownload from this torrent", cancellationToken);
                throw new ResetTorrentReusedException($"'{order.Title}' was reset for redownload from this torrent; choose another candidate.");
            }

            if (!string.IsNullOrWhiteSpace(addedTorrent.Hash))
            {
                onTorrentAdded?.Invoke(addedTorrent);
            }

            if (_blacklistService.IsBlacklisted(order.MediaId, order.SelectedCandidateUrl, addedTorrent.Hash))
            {
                _logger.Warning(
                    $"Skipped add — infohash already blacklisted for '{order.Title}' (hash={addedTorrent.Hash}).",
                    LogTarget.File | LogTarget.Console);

                finished = true;
                await DeleteRejectedTorrentAsync(
                    order,
                    addedTorrent.Hash,
                    cleanupReason: "blacklisted infohash",
                    blacklistReason: "Blacklisted infohash",
                    notes: $"Listing URL resolved to known-bad infohash '{addedTorrent.Hash}'",
                    rejectMessage: "Listing matches a blacklisted infohash.",
                    cancellationToken);
            }

            if (!validationEnabled)
            {
                // SEC-A09: the switch lives only in settings.json; make every unchecked add visible.
                _logger.Warning(
                    $"Content validation is disabled in settings: '{order.Title}' (hash={addedTorrent.Hash}) was added without checking its files.",
                    LogTarget.All);
                finished = true;
                return await GetLiveTorrentAsync(addedTorrent, cancellationToken);
            }

            var files = await WaitForTorrentFilesAsync(addedTorrent.Hash, cancellationToken);
            if (files.Count == 0)
            {
                _logger.Warning(
                    $"Metadata timeout for '{order.Title}' (hash={addedTorrent.Hash}): empty file list.",
                    LogTarget.File | LogTarget.Console);
                finished = true;
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

            var listingName = FirstNonEmpty(addedTorrent.Name, order.SelectedCandidateName, order.Title);
            var validation = await _contentValidationService.ValidateFilesAsync(
                addedTorrent.Hash,
                files,
                cancellationToken,
                listingName,
                isPack,
                order.TargetKind);

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

                finished = true;
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

            if (addedByGateHash is not null)
            {
                await SkipUnwantedFilesAsync(addedTorrent.Hash, files, validationConfig, cancellationToken);
                if (startAfterValidation)
                {
                    await _qbittorrentClient.ResumeTorrentsAsync([addedTorrent.Hash], cancellationToken);
                }
            }

            finished = true;
            _logger.Info(
                $"Validation passed for '{order.Title}' — download {(startAfterValidation ? "started" : "continues")} (hash={addedTorrent.Hash}).",
                LogTarget.File | LogTarget.Console);

            return await GetLiveTorrentAsync(addedTorrent, cancellationToken);
        }
        finally
        {
            if (addedByGateHash is not null && !finished)
            {
                // SEC-A02: canceled, crashed into an exception or lost qBittorrent mid-check. Never leave an unchecked
                // download running: stop it (the caller keeps its pending hash so it is re-checked, not adopted).
                await StopUncheckedTorrentAsync(order, addedByGateHash);
            }
        }
    }

    /// <summary>S0: the file list was read before the add; reject without adding anything.</summary>
    private async Task CheckBeforeAddAsync(
        TorrentCartOrder order,
        PreAddInspection inspection,
        bool isPack,
        Func<string?, bool>? isResetHash,
        CancellationToken cancellationToken)
    {
        if (inspection.MetadataTimedOut)
        {
            _logger.Warning(
                $"Metadata timeout before add for '{order.Title}' (hash={inspection.InfoHash}); nothing was added.",
                LogTarget.File | LogTarget.Console);
            throw new InvalidOperationException($"Torrent metadata timed out before it was added (hash={inspection.InfoHash}).");
        }

        if (isResetHash is not null && isResetHash(inspection.InfoHash))
        {
            throw new ResetTorrentReusedException($"'{order.Title}' was reset for redownload from this torrent; choose another candidate.");
        }

        if (_blacklistService.IsBlacklisted(order.MediaId, order.SelectedCandidateUrl, inspection.InfoHash))
        {
            throw new MaliciousTorrentException("Listing matches a blacklisted infohash.");
        }

        var validation = await _contentValidationService.ValidateFilesAsync(
            inspection.InfoHash ?? "(not added)",
            inspection.Files,
            cancellationToken,
            FirstNonEmpty(inspection.Name, order.SelectedCandidateName, order.Title),
            isPack,
            order.TargetKind);
        if (validation.IsValid || validation.Recommendation != TorrentHandleRecommendation.Delete)
        {
            _logger.Info(
                $"Checked before add for '{order.Title}' (hash={inspection.InfoHash}): {inspection.Files.Count} file(s), passed.",
                LogTarget.File | LogTarget.Console);
            return;
        }

        _logger.Warning(
            $"Rejected before add for '{order.Title}' (hash={inspection.InfoHash}): {validation.Summary}. Nothing was downloaded.",
            LogTarget.File | LogTarget.Console);
        await TryBlacklistAsync(
            order,
            inspection.InfoHash ?? string.Empty,
            $"Malware: {validation.Summary}",
            $"Rejected candidate '{order.SelectedCandidateName}' before add",
            validation.SuspiciousFiles);
        throw new MaliciousTorrentException(validation.Summary);
    }

    /// <summary>S0 (SEC-A04): files outside the download allow-list (.url, .nfo, images, archives...) get priority 0.</summary>
    private async Task SkipUnwantedFilesAsync(
        string hash,
        IReadOnlyList<TorrentContentFile> files,
        TorrentValidationConfig config,
        CancellationToken cancellationToken)
    {
        var unwanted = files
            .Select((file, index) => (file, index))
            .Where(item => !TorrentDownloadAllowList.IsWanted(item.file.Name, config))
            .Select(item => item.index)
            .ToList();
        if (unwanted.Count == 0)
        {
            return;
        }

        await _qbittorrentClient.SetFilePrioritiesAsync(hash, unwanted, 0, cancellationToken);
        _logger.Info($"Not downloading {unwanted.Count} extra file(s) of torrent {hash} (not video or subtitles).", LogTarget.File);
    }

    private async Task StopUncheckedTorrentAsync(TorrentCartOrder order, string hash)
    {
        try
        {
            await _qbittorrentClient.PauseTorrentsAsync([hash], CancellationToken.None);
            _logger.Warning(
                $"Stopped torrent {hash} for '{order.Title}': the add ended before its files were checked.",
                LogTarget.File | LogTarget.Console);
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Could not stop unchecked torrent {hash} for '{order.Title}': {ex.Message}",
                ex,
                LogTarget.File | LogTarget.Console);
        }
    }

    private int MetadataTimeoutSeconds() => Math.Clamp(
        _settingsService.Current.TorrentValidation?.ValidationTimeoutSeconds ?? 90,
        5,
        120);

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
                notes,
                global: true);
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
        var deadline = DateTime.UtcNow.AddSeconds(MetadataTimeoutSeconds());

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
