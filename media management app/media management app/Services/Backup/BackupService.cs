using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.Services.Backup;

public sealed class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ISettingsService _settingsService;
    private readonly IDatabaseService _databaseService;
    private readonly IGoogleDriveClient _driveClient;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;

    public event EventHandler? RunStateChanged;

    public BackupService(
        ISettingsService settingsService,
        IDatabaseService databaseService,
        IGoogleDriveClient driveClient,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;
        _driveClient = driveClient;
        _logger = logger;
    }

    public async Task<BackupRunResult> RunBackupAsync(BackupTriggerType triggerType, CancellationToken cancellationToken = default)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return BackupRunResult.Failure(triggerType, "A backup is already running.");
        }

        _isRunning = true;
        RunStateChanged?.Invoke(this, EventArgs.Empty);

        var tempDir = Path.Combine(Path.GetTempPath(), "MediaManagerBackup", Guid.NewGuid().ToString("N"));
        try
        {
            if (!await _driveClient.TryEnsureConnectedSilentlyAsync(cancellationToken).ConfigureAwait(false))
            {
                return Fail(triggerType, "Not connected to Google Drive. Connect via Settings > Backup first.");
            }

            var backup = _settingsService.Current.Backup;
            var (rootId, machineId, historyId) = await EnsureFolderHierarchyAsync(cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(tempDir);
            var checksums = await BuildBackupContentsAsync(tempDir, cancellationToken).ConfigureAwait(false);

            var manifest = new BackupManifest
            {
                AppVersion = GetAppVersion(),
                TimestampUtc = DateTime.UtcNow,
                TriggerType = triggerType,
                MachineId = backup.MachineId,
                MachineName = Environment.MachineName,
                Checksums = checksums
            };
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, AppConstants.BackupManifestFileName),
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken).ConfigureAwait(false);

            var zipPath = Path.Combine(Path.GetTempPath(), "MediaManagerBackup", $"{Guid.NewGuid():N}.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            try
            {
                var latestFileId = await _driveClient.UploadOrUpdateFileAsync(
                    zipPath,
                    AppConstants.BackupLatestFileName,
                    machineId,
                    backup.LatestFileId,
                    cancellationToken).ConfigureAwait(false);
                backup.LatestFileId = latestFileId;

                var historyFileName = $"{manifest.TimestampUtc:yyyy-MM-dd_HHmmss}.zip";
                await _driveClient.UploadOrUpdateFileAsync(
                    zipPath,
                    historyFileName,
                    historyId,
                    existingFileId: null,
                    cancellationToken).ConfigureAwait(false);

                await PruneHistoryAsync(historyId, backup.HistoryRetentionCount, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TryDeleteFile(zipPath);
            }

            backup.LastBackupUtc = manifest.TimestampUtc;
            backup.LastBackupSucceeded = true;
            backup.LastBackupError = null;
            _settingsService.Save();

            var summary = $"Backup ({triggerType}) uploaded to Google Drive at {manifest.TimestampUtc:u}.";
            _logger.Info(summary, LogTarget.All);
            return BackupRunResult.Success(triggerType, summary);
        }
        catch (Exception ex)
        {
            return Fail(triggerType, $"Backup failed: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
            _runLock.Release();
            _isRunning = false;
            RunStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<IReadOnlyList<DriveFileInfo>> ListHistoryAsync(CancellationToken cancellationToken = default)
    {
        var historyId = _settingsService.Current.Backup.DriveHistoryFolderId;
        if (string.IsNullOrWhiteSpace(historyId))
        {
            return [];
        }

        if (!await _driveClient.TryEnsureConnectedSilentlyAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var files = await _driveClient.ListFilesInFolderAsync(historyId, cancellationToken).ConfigureAwait(false);
        return files.OrderByDescending(file => file.CreatedTimeUtc).ToList();
    }

    public async Task<string> RestoreAsync(string driveFileId, CancellationToken cancellationToken = default)
    {
        if (!await _driveClient.TryEnsureConnectedSilentlyAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Not connected to Google Drive.");
        }

        var stateFolder = _settingsService.Current.StateFolder;
        var zipPath = Path.Combine(Path.GetTempPath(), "MediaManagerRestore", $"{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        try
        {
            await _driveClient.DownloadFileAsync(driveFileId, zipPath, cancellationToken).ConfigureAwait(false);
            var result = ApplyLocalBackup(zipPath, stateFolder, refuseLiveLegacyTarget: false);
            _logger.Info($"Restore completed from Drive file '{driveFileId}'.", LogTarget.All);
            return result.ReviewSettingsPath;
        }
        finally
        {
            TryDeleteFile(zipPath);
        }
    }

    public LocalBackupApplyResult ApplyLocalBackup(
        string sourceZipOrFolder,
        string targetStateFolder,
        bool refuseLiveLegacyTarget = false)
    {
        if (string.IsNullOrWhiteSpace(sourceZipOrFolder))
        {
            throw new ArgumentException("Backup source is empty.", nameof(sourceZipOrFolder));
        }

        if (string.IsNullOrWhiteSpace(targetStateFolder))
        {
            throw new ArgumentException("Target state folder is empty.", nameof(targetStateFolder));
        }

        var source = Path.GetFullPath(sourceZipOrFolder.Trim());
        var target = Path.GetFullPath(targetStateFolder.Trim());
        if (refuseLiveLegacyTarget && PathsEqual(target, AppConstants.LegacyStateFolder))
        {
            throw new InvalidOperationException(
                "This pinned test launch cannot restore into the live D:\\MediaManagerState folder.");
        }

        var sourceIsDirectory = Directory.Exists(source);
        if (sourceIsDirectory && PathsEqual(source, target))
        {
            throw new InvalidOperationException("Backup source and state folder cannot be the same path.");
        }

        Directory.CreateDirectory(target);

        string? staging = null;
        try
        {
            string payload;
            if (sourceIsDirectory)
            {
                payload = ResolvePayloadDirectory(source);
            }
            else if (File.Exists(source))
            {
                staging = Path.Combine(Path.GetTempPath(), "MediaManagerRestore", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                ZipFile.ExtractToDirectory(source, staging, overwriteFiles: true);
                payload = ResolvePayloadDirectory(staging);
            }
            else
            {
                throw new FileNotFoundException($"Backup source not found: {source}", source);
            }

            var dbSource = Path.Combine(payload, AppConstants.BackupDatabaseEntryName);
            var settingsSource = Path.Combine(payload, AppConstants.BackupSettingsEntryName);
            var recipesSource = Path.Combine(payload, AppConstants.BackupRecipesEntryFolderName);
            if (!File.Exists(dbSource) && !File.Exists(settingsSource) && !Directory.Exists(recipesSource))
            {
                throw new InvalidOperationException(
                    "Not a Media Manager backup (missing media-manager.db and settings.json).");
            }

            var databaseCopied = false;
            if (File.Exists(dbSource))
            {
                SqliteConnection.ClearAllPools();
                var targetDb = Path.Combine(target, AppConstants.BackupDatabaseEntryName);
                File.Copy(dbSource, targetDb, overwrite: true);
                _databaseService.Initialize(target);
                databaseCopied = true;
            }

            if (Directory.Exists(recipesSource))
            {
                var targetRecipesDir = Path.Combine(target, AppConstants.BackupRecipesEntryFolderName);
                Directory.CreateDirectory(targetRecipesDir);
                foreach (var file in Directory.EnumerateFiles(recipesSource))
                {
                    File.Copy(file, Path.Combine(targetRecipesDir, Path.GetFileName(file)), overwrite: true);
                }
            }

            var reviewPath = Path.Combine(target, AppConstants.BackupRestoredSettingsReviewFileName);
            AppSettings? reviewSettings = null;
            if (File.Exists(settingsSource))
            {
                File.Copy(settingsSource, reviewPath, overwrite: true);
                try
                {
                    var json = File.ReadAllText(settingsSource);
                    reviewSettings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                }
                catch
                {
                    reviewSettings = null;
                }
            }

            return new LocalBackupApplyResult
            {
                ReviewSettingsPath = reviewPath,
                ReviewSettings = reviewSettings,
                SourceHasSecrets = BackupSettingsRedactor.HasSecrets(reviewSettings),
                DatabaseCopied = databaseCopied
            };
        }
        finally
        {
            if (staging is not null)
            {
                TryDeleteDirectory(staging);
            }
        }
    }

    private static string ResolvePayloadDirectory(string root)
    {
        if (File.Exists(Path.Combine(root, AppConstants.BackupSettingsEntryName))
            || File.Exists(Path.Combine(root, AppConstants.BackupDatabaseEntryName)))
        {
            return root;
        }

        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories(root);
        }
        catch
        {
            return root;
        }

        if (dirs.Length != 1)
        {
            return root;
        }

        var nested = dirs[0];
        if (File.Exists(Path.Combine(nested, AppConstants.BackupSettingsEntryName))
            || File.Exists(Path.Combine(nested, AppConstants.BackupDatabaseEntryName)))
        {
            return nested;
        }

        return root;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left.Trim()),
                Path.GetFullPath(right.Trim()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<(string RootId, string MachineId, string HistoryId)> EnsureFolderHierarchyAsync(CancellationToken cancellationToken)
    {
        var backup = _settingsService.Current.Backup;
        var changed = false;

        if (string.IsNullOrWhiteSpace(backup.DriveRootFolderId))
        {
            backup.DriveRootFolderId = await _driveClient.EnsureFolderAsync(
                AppConstants.BackupDriveRootFolderName, null, cancellationToken).ConfigureAwait(false);
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(backup.DriveMachineFolderId))
        {
            backup.DriveMachineFolderId = await _driveClient.EnsureFolderAsync(
                backup.MachineId, backup.DriveRootFolderId, cancellationToken).ConfigureAwait(false);
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(backup.DriveHistoryFolderId))
        {
            backup.DriveHistoryFolderId = await _driveClient.EnsureFolderAsync(
                AppConstants.BackupDriveHistoryFolderName, backup.DriveMachineFolderId, cancellationToken).ConfigureAwait(false);
            changed = true;
        }

        if (changed)
        {
            _settingsService.Save();
        }

        return (backup.DriveRootFolderId!, backup.DriveMachineFolderId!, backup.DriveHistoryFolderId!);
    }

    private async Task<Dictionary<string, string>> BuildBackupContentsAsync(string tempDir, CancellationToken cancellationToken)
    {
        var checksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var dbPath = Path.Combine(tempDir, AppConstants.BackupDatabaseEntryName);
        _databaseService.CreateSafeSnapshot(dbPath);
        checksums[AppConstants.BackupDatabaseEntryName] = await ComputeSha256Async(dbPath, cancellationToken).ConfigureAwait(false);

        var settingsPath = Path.Combine(tempDir, AppConstants.BackupSettingsEntryName);
        var redactedJson = BackupSettingsRedactor.ToRedactedJson(_settingsService.Current);
        await File.WriteAllTextAsync(settingsPath, redactedJson, cancellationToken).ConfigureAwait(false);
        checksums[AppConstants.BackupSettingsEntryName] = await ComputeSha256Async(settingsPath, cancellationToken).ConfigureAwait(false);

        var recipesSourceDir = Path.Combine(_settingsService.Current.StateFolder, AppConstants.BackupRecipesEntryFolderName);
        if (Directory.Exists(recipesSourceDir))
        {
            var recipesTargetDir = Path.Combine(tempDir, AppConstants.BackupRecipesEntryFolderName);
            Directory.CreateDirectory(recipesTargetDir);
            foreach (var file in Directory.EnumerateFiles(recipesSourceDir, "*.rcp"))
            {
                var targetPath = Path.Combine(recipesTargetDir, Path.GetFileName(file));
                File.Copy(file, targetPath, overwrite: true);
                var entryName = $"{AppConstants.BackupRecipesEntryFolderName}/{Path.GetFileName(file)}";
                checksums[entryName] = await ComputeSha256Async(targetPath, cancellationToken).ConfigureAwait(false);
            }
        }

        return checksums;
    }

    private async Task PruneHistoryAsync(string historyFolderId, int retentionCount, CancellationToken cancellationToken)
    {
        var files = await _driveClient.ListFilesInFolderAsync(historyFolderId, cancellationToken).ConfigureAwait(false);
        var toDelete = files
            .OrderBy(file => file.CreatedTimeUtc)
            .Take(Math.Max(0, files.Count - retentionCount));

        foreach (var file in toDelete)
        {
            try
            {
                await _driveClient.DeleteFileAsync(file.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to prune old backup '{file.Name}': {ex.Message}", LogTarget.File | LogTarget.Console);
            }
        }
    }

    private BackupRunResult Fail(BackupTriggerType triggerType, string summary)
    {
        var backup = _settingsService.Current.Backup;
        backup.LastBackupSucceeded = false;
        backup.LastBackupError = summary;
        _settingsService.Save();
        _logger.Warning(summary, LogTarget.All);
        return BackupRunResult.Failure(triggerType, summary);
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string GetAppVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
