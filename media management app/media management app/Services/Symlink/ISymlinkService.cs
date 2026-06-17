namespace media_management_app.Services.Symlink;

public interface ISymlinkService
{
    bool IsRunningAsAdministrator();

    bool TryCreateFileSymlink(string symlinkPath, string targetPath, out string? errorMessage);

    bool TryRemoveSymlink(string symlinkPath, out string? errorMessage);

    bool TryResolveSymlinkTarget(string symlinkPath, out string? targetPath);

    bool IsSymlink(string path);
}
