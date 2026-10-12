namespace media_management_app.Services.Backup;

public interface IGoogleDriveClient
{
    /// <summary>True once a cached OAuth token exists on disk. Does not guarantee the token is still valid.</summary>
    bool HasStoredCredential { get; }

    /// <summary>Path to the OAuth client credentials.json the user drops into the state folder.</summary>
    string CredentialsFilePath { get; }

    /// <summary>Runs the interactive OAuth consent flow (opens a browser). Only call this from a user-initiated action, never from a background scheduler. Times out if the browser never completes the redirect (e.g. access_denied / closed tab).</summary>
    Task<GoogleDriveConnectResult> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Attempts to silently obtain a valid, cached credential (refreshing if needed) without prompting the user. Safe to call from background jobs.</summary>
    Task<bool> TryEnsureConnectedSilentlyAsync(CancellationToken cancellationToken = default);

    Task<string> EnsureFolderAsync(string name, string? parentFolderId, CancellationToken cancellationToken = default);

    /// <summary>Creates the file if <paramref name="existingFileId"/> is null/empty, otherwise updates that file's content in place. Returns the resulting file id.</summary>
    Task<string> UploadOrUpdateFileAsync(
        string localFilePath,
        string fileName,
        string parentFolderId,
        string? existingFileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DriveFileInfo>> ListFilesInFolderAsync(string parentFolderId, CancellationToken cancellationToken = default);

    Task DeleteFileAsync(string fileId, CancellationToken cancellationToken = default);

    Task DownloadFileAsync(string fileId, string destinationFilePath, CancellationToken cancellationToken = default);
}
