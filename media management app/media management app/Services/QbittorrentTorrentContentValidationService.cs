using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// App wrapper: fetches qBittorrent file lists then delegates to Core file-list validation.
/// </summary>
public sealed class QbittorrentTorrentContentValidationService : ITorrentContentValidationService
{
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly TorrentContentValidationService _fileListValidator;

    public QbittorrentTorrentContentValidationService(
        IQbittorrentClient qbittorrentClient,
        ISettingsService settingsService,
        IAppLogger logger)
    {
        _qbittorrentClient = qbittorrentClient;
        _fileListValidator = new TorrentContentValidationService(
            () => settingsService.Current.TorrentValidation ?? new TorrentValidationConfig(),
            new AppLoggerAdapter(logger));
    }

    public async Task<TorrentContentValidationResult> ValidateAsync(
        string torrentHash,
        CancellationToken cancellationToken = default)
    {
        var files = await _qbittorrentClient.GetTorrentFilesAsync(torrentHash, cancellationToken);
        return await ValidateFilesAsync(torrentHash, files, cancellationToken);
    }

    public Task<TorrentContentValidationResult> ValidateFilesAsync(
        string torrentHash,
        IReadOnlyList<TorrentContentFile> files,
        CancellationToken cancellationToken = default,
        string? listingName = null,
        bool isPack = false) =>
        _fileListValidator.ValidateFilesAsync(torrentHash, files, cancellationToken, listingName, isPack);

    private sealed class AppLoggerAdapter(IAppLogger logger) : ITorrentContentValidationLogger
    {
        public void Debug(string message) => logger.Debug(message, LogTarget.File);

        public void Info(string message) => logger.Info(message, LogTarget.File | LogTarget.Console);

        public void Warning(string message) => logger.Warning(message, LogTarget.All);
    }
}
