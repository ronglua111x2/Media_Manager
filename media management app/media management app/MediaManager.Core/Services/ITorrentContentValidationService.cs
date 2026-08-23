using media_management_app.Models;

namespace media_management_app.Services;

public interface ITorrentContentValidationService
{
    Task<TorrentContentValidationResult> ValidateAsync(
        string torrentHash,
        CancellationToken cancellationToken = default);

    Task<TorrentContentValidationResult> ValidateFilesAsync(
        string torrentHash,
        IReadOnlyList<TorrentContentFile> files,
        CancellationToken cancellationToken = default,
        string? listingName = null,
        bool isPack = false);
}

public interface ITorrentContentValidationLogger
{
    void Debug(string message);

    void Info(string message);

    void Warning(string message);
}
