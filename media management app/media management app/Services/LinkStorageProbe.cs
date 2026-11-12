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
    /// or when the item has no link. The anchor is the generated <c>Shows</c>/<c>Movies</c> folder
    /// above the link; links outside that layout fall back to their drive root.
    /// </summary>
    public bool IsLinkStorageReachable(SourceItem item)
    {
        if (string.IsNullOrWhiteSpace(item.LinkedPath))
        {
            return true;
        }

        return IsFolderReachable(FindLibraryAnchor(item.LinkedPath));
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
                return directory;
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
