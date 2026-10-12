using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using media_management_app.Common;

namespace media_management_app.Services.Symlink;

public sealed class SymlinkService : ISymlinkService
{
    private const int SymbolicLinkFlagFile = 0x0;

    private readonly IAppLogger _logger;

    public SymlinkService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public bool TryCreateFileSymlink(string symlinkPath, string targetPath, out string? errorMessage)
    {
        errorMessage = null;

        if (!IsRunningAsAdministrator())
        {
            errorMessage = "Administrator privileges are required to create symlinks.";
            _logger.Warning(errorMessage, LogTarget.All);
            return false;
        }

        var fullSymlinkPath = Path.GetFullPath(symlinkPath);
        var fullTargetPath = Path.GetFullPath(targetPath);

        if (string.Equals(fullSymlinkPath, fullTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Symlink path cannot be the same as the target path.";
            return false;
        }

        if (!File.Exists(fullTargetPath))
        {
            errorMessage = $"Symlink target does not exist: {fullTargetPath}";
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullSymlinkPath)!);

        if (File.Exists(fullSymlinkPath) || Directory.Exists(fullSymlinkPath))
        {
            if (!TryRemoveSymlink(fullSymlinkPath, out errorMessage))
            {
                return false;
            }
        }

        _logger.Info($"Creating symlink {fullSymlinkPath} -> {fullTargetPath}", LogTarget.File | LogTarget.Console);
        var ok = CreateSymbolicLinkNative(ToExtendedLengthPath(fullSymlinkPath), ToExtendedLengthPath(fullTargetPath), SymbolicLinkFlagFile);
        if (!ok)
        {
            var errorCode = Marshal.GetLastWin32Error();
            errorMessage = $"{new Win32Exception(errorCode).Message} (Win32 error {errorCode})";
            _logger.Error($"Symlink creation failed: {errorMessage}", targets: LogTarget.All);
            return false;
        }

        _logger.Info($"Created symlink: {fullSymlinkPath}", LogTarget.All);
        return true;
    }

    public bool TryRemoveSymlink(string symlinkPath, out string? errorMessage)
    {
        errorMessage = null;
        var fullPath = Path.GetFullPath(symlinkPath);

        if (!File.Exists(fullPath))
        {
            return true;
        }

        try
        {
            File.Delete(fullPath);
            _logger.Info($"Removed symlink: {fullPath}", LogTarget.All);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _logger.Error($"Could not remove symlink: {fullPath}", ex, LogTarget.All);
            return false;
        }
    }

    public bool TryResolveSymlinkTarget(string symlinkPath, out string? targetPath)
    {
        targetPath = null;
        if (!File.Exists(symlinkPath))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(symlinkPath);
            if (!info.Exists)
            {
                return false;
            }

            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            if (resolved is null)
            {
                return false;
            }

            targetPath = resolved.FullName;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not resolve symlink target for {symlinkPath}: {ex.Message}", LogTarget.All);
            return false;
        }
    }

    public bool IsSymlink(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static string ToExtendedLengthPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return $@"\\?\UNC\{fullPath[2..]}";
        }

        return $@"\\?\{fullPath}";
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateSymbolicLinkNative(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);
}
