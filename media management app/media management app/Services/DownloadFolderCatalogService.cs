using media_management_app.ViewModels;

namespace media_management_app.Services;

public sealed class DownloadFolderCatalogService : IDownloadFolderCatalogService
{
    private readonly ISettingsService _settingsService;

    public DownloadFolderCatalogService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public IReadOnlyList<string> GetKnownDownloadFolders()
    {
        return GetKnownDownloadFoldersInternal().ToList();
    }

    public IReadOnlyList<DownloadFolderOption> GetDownloadFolderOptions()
    {
        return GetKnownDownloadFoldersInternal()
            .Select(folder =>
            {
                var driveRoot = Path.GetPathRoot(folder) ?? folder;
                var folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    folderName = folder;
                }

                return new DownloadFolderOption
                {
                    Folder = folder,
                    DriveRoot = driveRoot,
                    DisplayLabel = $"{driveRoot} — {folderName}"
                };
            })
            .ToList();
    }

    public IReadOnlyList<StorageStatusViewModel> GetDriveStatuses()
    {
        var statuses = new List<StorageStatusViewModel>();
        foreach (var folder in GetKnownDownloadFoldersInternal().Where(Directory.Exists))
        {
            var status = TryGetDriveStatus(folder);
            if (status is null)
            {
                continue;
            }

            if (statuses.Any(existing => string.Equals(existing.DriveRoot, status.DriveRoot, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            statuses.Add(status);
        }

        return statuses;
    }

    private IEnumerable<string> GetKnownDownloadFoldersInternal()
    {
        var settings = _settingsService.Current;
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.AutoTorrent.DownloadFolder))
        {
            folders.Add(settings.AutoTorrent.DownloadFolder.Trim());
        }

        folders.AddRange(settings.AutoTorrent.DownloadFolders.Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(folder => folder.Trim()));
        folders.AddRange(settings.SourceFolders.Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(folder => folder.Trim()));

        return folders
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static StorageStatusViewModel? TryGetDriveStatus(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(folder);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return null;
            }

            return new StorageStatusViewModel
            {
                DriveRoot = drive.RootDirectory.FullName,
                Folder = folder,
                TotalBytes = drive.TotalSize,
                FreeBytes = drive.AvailableFreeSpace
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
