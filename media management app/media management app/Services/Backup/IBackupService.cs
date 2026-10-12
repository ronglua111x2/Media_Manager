using media_management_app.Common;

namespace media_management_app.Services.Backup;

public interface IBackupService
{
    /// <summary>True while a backup upload is actively running.</summary>
    bool IsRunning { get; }

    /// <summary>Raised whenever <see cref="IsRunning"/> changes.</summary>
    event EventHandler? RunStateChanged;

    Task<BackupRunResult> RunBackupAsync(BackupTriggerType triggerType, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DriveFileInfo>> ListHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and extracts a backup zip. The database and Recipes are restored in place; settings.json from the
    /// backup (secrets already excluded) is written next to the live settings file for manual review instead of
    /// being applied automatically, so a restore never silently wipes locally-configured secrets.
    /// </summary>
    Task<string> RestoreAsync(string driveFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies <c>media-manager.db</c> and <c>Recipes/</c> from a local zip or copied state folder into
    /// <paramref name="targetStateFolder"/>. Writes <c>restored-settings.review.json</c>; does not overwrite
    /// the live <c>settings.json</c>. When <paramref name="refuseLiveLegacyTarget"/> is true, refuses the
    /// live <c>D:\MediaManagerState</c> folder (pinned first-run tests).
    /// </summary>
    LocalBackupApplyResult ApplyLocalBackup(
        string sourceZipOrFolder,
        string targetStateFolder,
        bool refuseLiveLegacyTarget = false);
}
