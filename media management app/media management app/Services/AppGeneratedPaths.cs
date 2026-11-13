using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Files the app itself created: library hardlinks of other records and Jellyfin symlinks.
/// Importing them again made a second record share one library file, so removing the media
/// later left the other record behind and its symlink came back on the next sync (PARSE-30).
/// </summary>
public static class AppGeneratedPaths
{
    public static HashSet<string> Collect(IEnumerable<SourceItem> items)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.LinkedPath) &&
                !string.Equals(Normalize(item.LinkedPath), Normalize(item.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(Normalize(item.LinkedPath));
            }

            if (!string.IsNullOrWhiteSpace(item.SymlinkPath))
            {
                paths.Add(Normalize(item.SymlinkPath));
            }
        }

        return paths;
    }

    /// <summary>
    /// Download files a torrent record already links into the library. Importing one again overwrote that record
    /// (the upsert is keyed on the file path): the library hardlink lost its record and later blocked every re-link
    /// with "Output already exists" (audit R2-A-06).
    /// </summary>
    public static HashSet<string> CollectLinkedDownloads(IEnumerable<SourceItem> items)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!item.IsExternalImport &&
                !string.IsNullOrWhiteSpace(item.FilePath) &&
                !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                !string.Equals(Normalize(item.LinkedPath), Normalize(item.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(Normalize(item.FilePath));
            }
        }

        return paths;
    }

    public static bool IsGenerated(IReadOnlySet<string> generatedPaths, string path) =>
        !string.IsNullOrWhiteSpace(path) && generatedPaths.Contains(Normalize(path));

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
