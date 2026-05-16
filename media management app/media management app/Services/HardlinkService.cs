using System.Runtime.InteropServices;
using System.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class HardlinkService : IHardlinkService
{
    private readonly IAppLogger _logger;

    public HardlinkService(IAppLogger logger)
    {
        _logger = logger;
    }

    public string BuildOutputPath(SourceItem item, string outputRoot)
    {
        if (item.MediaKind == MediaKind.Movie)
        {
            var movieFolderName = BuildMovieFolderName(item);
            var extension = Path.GetExtension(item.FilePath);
            return Path.Combine(outputRoot, movieFolderName, $"{movieFolderName}{extension}");
        }

        var showName = Sanitize(item.ShowTitle ?? "Unknown Show");
        var seasonFolder = $"Season {item.SeasonNumber.GetValueOrDefault():00}";
        var fileName = $"{showName} - S{item.SeasonNumber.GetValueOrDefault():00}E{item.EpisodeNumber.GetValueOrDefault():00}{Path.GetExtension(item.FilePath)}";
        return Path.Combine(outputRoot, showName, seasonFolder, fileName);
    }

    public bool CreateHardLink(SourceItem item, string outputRoot, out string? createdPath, out string? errorMessage)
    {
        createdPath = null;
        errorMessage = null;

        var targetPath = BuildOutputPath(item, outputRoot);
        if (File.Exists(targetPath))
        {
            errorMessage = "Output already exists.";
            _logger.Warning($"Hardlink skipped because output exists: {targetPath}", LogTarget.All);
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        _logger.Info($"Creating hardlink from {item.FilePath} to {targetPath}", LogTarget.File | LogTarget.Console);
        var ok = CreateHardLinkNative(targetPath, item.FilePath, IntPtr.Zero);
        if (!ok)
        {
            var errorCode = Marshal.GetLastWin32Error();
            errorMessage = new Win32Exception(errorCode).Message;
            _logger.Error($"Hardlink failed: {errorMessage}", targets: LogTarget.All);
            return false;
        }

        createdPath = targetPath;
        _logger.Info($"Created hardlink: {targetPath}", LogTarget.All);
        return true;
    }

    public bool RemoveHardLink(SourceItem item, string outputRoot, out string? removedPath, out string? errorMessage)
    {
        removedPath = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(item.LinkedPath))
        {
            errorMessage = "Item does not have a linked path.";
            _logger.Warning($"Remove hardlink skipped because item has no linked path: {item.FilePath}", LogTarget.Ui | LogTarget.Console);
            return false;
        }

        if (string.Equals(Path.GetFullPath(item.LinkedPath), Path.GetFullPath(item.FilePath), StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Linked path matches the source path. Refusing to delete.";
            _logger.Error($"Refusing to delete source path while removing hardlink: {item.FilePath}", targets: LogTarget.All);
            return false;
        }

        removedPath = item.LinkedPath;
        if (!IsPathInsideRoot(item.LinkedPath, outputRoot))
        {
            errorMessage = "Linked path is outside the configured output library. Refusing to delete.";
            _logger.Error($"Refusing to delete linked path outside output root. LinkedPath={item.LinkedPath}; OutputRoot={outputRoot}", targets: LogTarget.All);
            return false;
        }

        var linkedDirectory = Path.GetDirectoryName(item.LinkedPath);
        if (!File.Exists(item.LinkedPath))
        {
            _logger.Warning($"Linked path no longer exists, clearing state only: {item.LinkedPath}", LogTarget.All);
            CleanupEmptyLibraryFolders(linkedDirectory, outputRoot);
            return true;
        }

        try
        {
            File.Delete(item.LinkedPath);
            _logger.Info($"Removed hardlink path: {item.LinkedPath}", LogTarget.All);
            CleanupEmptyLibraryFolders(linkedDirectory, outputRoot);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _logger.Error($"Could not remove hardlink path: {item.LinkedPath}", ex, LogTarget.All);
            return false;
        }
    }

    private void CleanupEmptyLibraryFolders(string? startDirectory, string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
        {
            return;
        }

        var root = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(startDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        while (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase) &&
               IsPathInsideRoot(current, root) &&
               Directory.Exists(current) &&
               !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current);
            _logger.Info($"Removed empty library folder: {current}", LogTarget.File | LogTarget.Ui | LogTarget.Console);

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string Sanitize(string value)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(ch, '_');
        }

        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string BuildMovieFolderName(SourceItem item)
    {
        var title = Sanitize(item.MovieTitle ?? item.ShowTitle ?? "Unknown Movie");
        return item.MovieYear is null ? title : $"{title} ({item.MovieYear})";
    }

    private static bool IsPathInsideRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLinkNative(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
