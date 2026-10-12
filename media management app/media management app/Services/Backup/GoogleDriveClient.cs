using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using media_management_app.Common;

namespace media_management_app.Services.Backup;

public sealed class GoogleDriveClient : IGoogleDriveClient
{
    private const string ApplicationName = "Media Manager Backup";
    private static readonly string[] Scopes = [DriveService.Scope.DriveFile];
    private const string DriveFolderMimeType = "application/vnd.google-apps.folder";

    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private DriveService? _driveService;

    public GoogleDriveClient(ISettingsService settingsService, IAppLogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public string CredentialsFilePath =>
        string.IsNullOrWhiteSpace(_settingsService.Current.Backup.CredentialsFilePath)
            ? Path.Combine(
                _settingsService.Current.StateFolder,
                AppConstants.BackupGoogleDriveFolderName,
                AppConstants.BackupCredentialsFileName)
            : _settingsService.Current.Backup.CredentialsFilePath;

    private string TokenFolderPath => Path.Combine(
        _settingsService.Current.StateFolder,
        AppConstants.BackupGoogleDriveFolderName,
        AppConstants.BackupTokenFolderName);

    public bool HasStoredCredential => Directory.Exists(TokenFolderPath) && Directory.EnumerateFileSystemEntries(TokenFolderPath).Any();

    public async Task<GoogleDriveConnectResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(AppConstants.GoogleDriveConnectTimeout);

        var authorizeTask = AuthorizeAsync(allowInteractive: true, timeoutCts.Token);
        try
        {
            // Hard UI timeout: GoogleWebAuthorizationBroker can hang forever when the browser
            // never redirects back (access_denied page / closed tab).
            var completed = await Task.WhenAny(
                authorizeTask,
                Task.Delay(AppConstants.GoogleDriveConnectTimeout, cancellationToken)).ConfigureAwait(false);

            if (completed != authorizeTask)
            {
                timeoutCts.Cancel();
                _ = ObserveAbandonedAuthorizeAsync(authorizeTask);
                _logger.Warning(
                    $"Google Drive connection timed out after {AppConstants.GoogleDriveConnectTimeout.TotalMinutes:0} minute(s) waiting for browser sign-in.",
                    LogTarget.All);
                return new GoogleDriveConnectResult(
                    false,
                    "Google Drive sign-in timed out. If Google showed access denied / unfinished verification, add your account as a Test user in Google Cloud Console, then click Connect again.");
            }

            var service = await authorizeTask.ConfigureAwait(false);
            if (service is null)
            {
                return new GoogleDriveConnectResult(
                    false,
                    "Could not connect to Google Drive. If your Google Cloud project is in Testing mode, add this Google account as a Test user on the OAuth consent screen, then try again.");
            }

            return new GoogleDriveConnectResult(true, "Connected to Google Drive.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warning(
                $"Google Drive connection timed out after {AppConstants.GoogleDriveConnectTimeout.TotalMinutes:0} minute(s) waiting for browser sign-in.",
                LogTarget.All);
            return new GoogleDriveConnectResult(
                false,
                "Google Drive sign-in timed out. If Google showed access denied / unfinished verification, add your account as a Test user in Google Cloud Console, then click Connect again.");
        }
        catch (OperationCanceledException)
        {
            return new GoogleDriveConnectResult(false, "Google Drive connection was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Google Drive connection failed: {ex.Message}", LogTarget.All);
            return new GoogleDriveConnectResult(false, FormatConnectFailureMessage(ex));
        }
    }

    public async Task<bool> TryEnsureConnectedSilentlyAsync(CancellationToken cancellationToken = default)
    {
        if (!HasStoredCredential)
        {
            return false;
        }

        return await AuthorizeAsync(allowInteractive: false, cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task<string> EnsureFolderAsync(string name, string? parentFolderId, CancellationToken cancellationToken = default)
    {
        var service = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);

        var query = $"mimeType = '{DriveFolderMimeType}' and name = '{EscapeQueryValue(name)}' and trashed = false";
        query += string.IsNullOrWhiteSpace(parentFolderId)
            ? " and 'root' in parents"
            : $" and '{EscapeQueryValue(parentFolderId)}' in parents";

        var listRequest = service.Files.List();
        listRequest.Q = query;
        listRequest.Fields = "files(id, name)";
        listRequest.Spaces = "drive";
        var existing = await listRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        var match = existing.Files?.FirstOrDefault();
        if (match is not null)
        {
            return match.Id;
        }

        var folderMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = name,
            MimeType = DriveFolderMimeType,
            Parents = string.IsNullOrWhiteSpace(parentFolderId) ? null : [parentFolderId]
        };

        var createRequest = service.Files.Create(folderMetadata);
        createRequest.Fields = "id";
        var created = await createRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info($"Created Google Drive folder '{name}' (id={created.Id}).", LogTarget.File | LogTarget.Console);
        return created.Id;
    }

    public async Task<string> UploadOrUpdateFileAsync(
        string localFilePath,
        string fileName,
        string parentFolderId,
        string? existingFileId,
        CancellationToken cancellationToken = default)
    {
        var service = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);

        await using var stream = File.OpenRead(localFilePath);
        const string contentType = "application/zip";

        if (!string.IsNullOrWhiteSpace(existingFileId))
        {
            var updateMetadata = new Google.Apis.Drive.v3.Data.File { Name = fileName };
            var updateRequest = service.Files.Update(updateMetadata, existingFileId, stream, contentType);
            updateRequest.Fields = "id";
            var updateProgress = await updateRequest.UploadAsync(cancellationToken).ConfigureAwait(false);
            if (updateProgress.Exception is not null)
            {
                throw updateProgress.Exception;
            }

            return updateRequest.ResponseBody?.Id ?? existingFileId;
        }

        var createMetadata = new Google.Apis.Drive.v3.Data.File { Name = fileName, Parents = [parentFolderId] };
        var createRequest = service.Files.Create(createMetadata, stream, contentType);
        createRequest.Fields = "id";
        var createProgress = await createRequest.UploadAsync(cancellationToken).ConfigureAwait(false);
        if (createProgress.Exception is not null)
        {
            throw createProgress.Exception;
        }

        return createRequest.ResponseBody?.Id
            ?? throw new InvalidOperationException($"Google Drive did not return an id after uploading '{fileName}'.");
    }

    public async Task<IReadOnlyList<DriveFileInfo>> ListFilesInFolderAsync(string parentFolderId, CancellationToken cancellationToken = default)
    {
        var service = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);

        var listRequest = service.Files.List();
        listRequest.Q = $"'{EscapeQueryValue(parentFolderId)}' in parents and trashed = false";
        listRequest.Fields = "files(id, name, createdTime)";
        listRequest.Spaces = "drive";
        listRequest.OrderBy = "createdTime";
        var result = await listRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);

        return (result.Files ?? [])
            .Select(file => new DriveFileInfo(file.Id, file.Name, file.CreatedTimeDateTimeOffset?.UtcDateTime))
            .ToList();
    }

    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var service = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        await service.Files.Delete(fileId).ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadFileAsync(string fileId, string destinationFilePath, CancellationToken cancellationToken = default)
    {
        var service = await RequireServiceAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath) ?? ".");

        await using var output = File.Create(destinationFilePath);
        await service.Files.Get(fileId).DownloadAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DriveService> RequireServiceAsync(CancellationToken cancellationToken)
    {
        if (_driveService is not null)
        {
            return _driveService;
        }

        var service = await AuthorizeAsync(allowInteractive: false, cancellationToken).ConfigureAwait(false);
        return service ?? throw new InvalidOperationException(
            "Not connected to Google Drive. Connect via Settings > Backup before running a backup.");
    }

    private async Task<DriveService?> AuthorizeAsync(bool allowInteractive, CancellationToken cancellationToken)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_driveService is not null)
            {
                return _driveService;
            }

            if (!File.Exists(CredentialsFilePath))
            {
                _logger.Warning(
                    $"Google Drive credentials.json not found at '{CredentialsFilePath}'.",
                    LogTarget.File | LogTarget.Console);
                return null;
            }

            if (!allowInteractive && !HasStoredCredential)
            {
                return null;
            }
        }
        finally
        {
            _connectLock.Release();
        }

        // Keep the long browser/OAuth wait outside the lock so a hung interactive connect
        // (access_denied page never redirects) cannot permanently block later attempts.
        try
        {
            Directory.CreateDirectory(TokenFolderPath);

            await using var stream = File.OpenRead(CredentialsFilePath);
            var secrets = (await GoogleClientSecrets.FromStreamAsync(stream, cancellationToken).ConfigureAwait(false)).Secrets;

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                Scopes,
                "media-manager",
                cancellationToken,
                new FileDataStore(TokenFolderPath, true)).ConfigureAwait(false);

            await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_driveService is not null)
                {
                    return _driveService;
                }

                _driveService = new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName
                });

                _logger.Info("Connected to Google Drive.", LogTarget.File | LogTarget.Console);
                return _driveService;
            }
            finally
            {
                _connectLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Google Drive authorization failed: {ex.Message}", LogTarget.All);
            return null;
        }
    }

    private async Task ObserveAbandonedAuthorizeAsync(Task<DriveService?> authorizeTask)
    {
        try
        {
            await authorizeTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected after ConnectAsync times out and cancels the token.
        }
        catch (Exception ex)
        {
            _logger.Warning($"Abandoned Google Drive authorize finished with error: {ex.Message}", LogTarget.File | LogTarget.Console);
        }
    }

    private static string FormatConnectFailureMessage(Exception ex)
    {
        var text = ex.Message ?? string.Empty;
        if (text.Contains("access_denied", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            return "Google denied access (403). Your OAuth app is likely still in Testing — add this Google account as a Test user in Google Cloud Console, then try Connect again.";
        }

        return $"Google Drive connection failed: {ex.Message}";
    }

    private static string EscapeQueryValue(string value) => value.Replace("'", "\\'");
}
