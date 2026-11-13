using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Tells "the file was deleted" apart from "the drive or folder is not reachable right now"
/// (external or network drive offline, BitLocker locked, drive mounted late). Reconciliation
/// must only clear or purge link records when the storage that holds them is reachable;
/// otherwise one offline drive wipes every link record on it (PARSE-27).
/// One probe instance caches folder checks for a single reconcile pass.
/// </summary>
public sealed class LinkStorageProbe
{
    private readonly Dictionary<string, bool> _folderExists = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unreachable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<SourceItem, string?>? _currentLibraryRoot;

    /// <param name="currentLibraryRoot">
    /// The library folder currently configured for an item's drive. With it, a link inside that folder is
    /// reachable when the folder is, and a link anywhere else (an old library folder after the setting changed, an
    /// imported path) only needs its drive or share: a deleted old library folder on a mounted drive is gone, not
    /// offline (audit R2-A-01). Without it the probe falls back to the folder above Shows/Movies.
    /// </param>
    public LinkStorageProbe(Func<SourceItem, string?>? currentLibraryRoot = null)
    {
        _currentLibraryRoot = currentLibraryRoot;
    }

    /// <summary>Folders found unreachable during this pass (for one log line per folder).</summary>
    public IReadOnlyCollection<string> UnreachableFolders => _unreachable;

    /// <summary>True when the download location that holds <see cref="SourceItem.FilePath"/> is reachable.</summary>
    public bool IsSourceStorageReachable(SourceItem item)
    {
        var anchor = !string.IsNullOrWhiteSpace(item.SourceRootFolder)
            ? item.SourceRootFolder
            : Path.GetPathRoot(item.FilePath);
        return IsFolderReachable(anchor);
    }

    /// <summary>
    /// True when the library location that holds <see cref="SourceItem.LinkedPath"/> is reachable,
    /// or when the item has no link. The anchor is the library folder that holds the generated
    /// <c>Shows</c>/<c>Movies</c> folder above the link, not that folder itself: the app's empty-folder
    /// cleanup deletes an empty <c>Shows</c> folder, and a reachable drive must not look offline then
    /// (audit L3-04). Links outside that layout fall back to their drive root.
    /// </summary>
    public bool IsLinkStorageReachable(SourceItem item)
    {
        if (string.IsNullOrWhiteSpace(item.LinkedPath))
        {
            return true;
        }

        if (_currentLibraryRoot is null)
        {
            return IsFolderReachable(FindLibraryAnchor(item.LinkedPath));
        }

        var libraryRoot = _currentLibraryRoot(item);
        return IsFolderReachable(
            !string.IsNullOrWhiteSpace(libraryRoot) && IsInside(item.LinkedPath, libraryRoot)
                ? libraryRoot
                : SafePathRoot(item.LinkedPath));
    }

    private static bool IsInside(string path, string root)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFullPath(path).StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string? SafePathRoot(string path)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    internal static string? FindLibraryAnchor(string linkedPath)
    {
        string? directory;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(linkedPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        while (!string.IsNullOrEmpty(directory))
        {
            var name = Path.GetFileName(directory);
            if (string.Equals(name, AppConstants.ShowsFolderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, AppConstants.MoviesFolderName, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(directory) ?? directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return Path.GetPathRoot(linkedPath);
    }

    private bool IsFolderReachable(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        if (!_folderExists.TryGetValue(folder, out var exists))
        {
            exists = Directory.Exists(folder);
            _folderExists[folder] = exists;
            if (!exists)
            {
                _unreachable.Add(folder);
            }
        }

        return exists;
    }
}
