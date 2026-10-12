using media_management_app.Common;
using media_management_app.Models;
using media_management_app.ViewModels;

namespace media_management_app.Services;

public sealed class TorrentAddDiskAssignmentService : ITorrentAddDiskAssignmentService
{
    private const long UnknownSizeBufferBytes = 2L * 1024 * 1024 * 1024;

    private readonly IDownloadFolderCatalogService _downloadFolderCatalogService;
    private readonly ISettingsService _settingsService;

    public TorrentAddDiskAssignmentService(
        IDownloadFolderCatalogService downloadFolderCatalogService,
        ISettingsService settingsService)
    {
        _downloadFolderCatalogService = downloadFolderCatalogService;
        _settingsService = settingsService;
    }

    public TorrentAddDiskPlan BuildPlan(
        IReadOnlyList<TorrentCartOrder> orders,
        Func<long, int, string?> getSeasonDownloadFolder)
    {
        var folderOptions = _downloadFolderCatalogService.GetKnownDownloadFolders();
        var defaultFolder = GetDefaultDownloadFolder();
        var driveStatuses = _downloadFolderCatalogService.GetDriveStatuses();

        var rows = orders.Select(order =>
        {
            var seasonFolder = order.TargetKind != MediaKind.Movie && order.SeasonNumber is not null
                ? getSeasonDownloadFolder(order.MediaId, order.SeasonNumber.Value)
                : null;
            var defaultDownloadFolder = FirstNonEmpty(seasonFolder, defaultFolder) ?? string.Empty;
            var planningSize = GetPlanningSize(order.SelectedCandidateFileSize);

            return new TorrentAddDiskRow
            {
                OrderId = order.Id,
                TargetKind = order.TargetKind,
                MediaId = order.MediaId,
                SeasonNumber = order.SeasonNumber,
                Title = order.Title,
                CandidateName = order.SelectedCandidateName,
                FileSizeBytes = order.SelectedCandidateFileSize,
                PlanningSizeBytes = planningSize,
                DefaultDownloadFolder = defaultDownloadFolder,
                SelectedDownloadFolder = defaultDownloadFolder,
                GroupKey = BuildGroupKey(order),
                GroupTitle = BuildGroupTitle(order)
            };
        }).ToList();

        var plan = new TorrentAddDiskPlan
        {
            Rows = rows,
            FolderOptions = folderOptions.ToList(),
            Drives = driveStatuses.Select(status => new TorrentAddDiskDriveSummary
            {
                DriveRoot = status.DriveRoot,
                Folder = status.Folder,
                TotalBytes = status.TotalBytes,
                FreeBytes = status.FreeBytes,
                IsLowSpace = status.IsLowSpace
            }).ToList(),
            TotalSizeBytes = rows.Sum(row => row.FileSizeBytes > 0 ? row.FileSizeBytes : 0)
        };

        Recalculate(plan);
        return plan;
    }

    public void AutoAssign(TorrentAddDiskPlan plan)
    {
        if (plan.Rows.Count == 0 || plan.FolderOptions.Count == 0)
        {
            Recalculate(plan);
            return;
        }

        var driveFree = plan.Drives.ToDictionary(
            drive => drive.DriveRoot,
            drive => drive.FreeBytes,
            StringComparer.OrdinalIgnoreCase);

        var groups = plan.Rows
            .GroupBy(row => row.GroupKey ?? $"order:{row.OrderId}")
            .Select(group => new AssignmentGroup
            {
                Key = group.Key,
                Rows = group.ToList(),
                SizeBytes = group.Sum(row => row.PlanningSizeBytes),
                PreferredFolder = GetPreferredFolder(group.ToList())
            })
            .OrderByDescending(group => group.SizeBytes)
            .ToList();

        foreach (var group in groups)
        {
            var assignedFolder = TryAssignPreferredFolder(group, driveFree, plan.FolderOptions)
                ?? TryAssignBestFitFolder(group.SizeBytes, driveFree, plan.FolderOptions);

            if (string.IsNullOrWhiteSpace(assignedFolder))
            {
                assignedFolder = group.PreferredFolder ?? plan.FolderOptions[0];
            }

            foreach (var row in group.Rows)
            {
                row.SelectedDownloadFolder = assignedFolder;
            }

            var driveRoot = Path.GetPathRoot(assignedFolder);
            if (!string.IsNullOrWhiteSpace(driveRoot) && driveFree.ContainsKey(driveRoot))
            {
                driveFree[driveRoot] = Math.Max(0, driveFree[driveRoot] - group.SizeBytes);
            }
        }

        Recalculate(plan);
    }

    public void Recalculate(TorrentAddDiskPlan plan)
    {
        foreach (var drive in plan.Drives)
        {
            drive.AssignedBytes = 0;
            drive.RemainingBytes = drive.FreeBytes;
            drive.IsOverflow = false;
        }

        foreach (var row in plan.Rows)
        {
            row.HasError = false;
            row.ErrorMessage = string.Empty;
        }

        foreach (var row in plan.Rows)
        {
            var drive = FindDriveForFolder(plan, row.SelectedDownloadFolder);
            if (drive is null)
            {
                row.HasError = true;
                row.ErrorMessage = "Selected folder is not on a known drive.";
                continue;
            }

            drive.AssignedBytes += row.PlanningSizeBytes;
        }

        foreach (var drive in plan.Drives)
        {
            drive.RemainingBytes = drive.FreeBytes - drive.AssignedBytes;
            drive.IsOverflow = drive.RemainingBytes < 0;
        }

        ValidateSeasonConsistency(plan);
        ValidateFolderAvailability(plan);
        MarkOverflowRows(plan);

        if (plan.Drives.Any(drive => drive.IsOverflow))
        {
            var driveList = string.Join(", ", plan.Drives.Where(drive => drive.IsOverflow).Select(drive => drive.DriveRoot));
            plan.ValidationMessage = $"Not enough free space on {driveList}. Reassign torrents or use Auto distribute.";
            plan.CanConfirm = false;
            return;
        }

        var invalidRows = plan.Rows.Where(row => row.HasError).ToList();
        if (invalidRows.Count > 0)
        {
            plan.ValidationMessage = invalidRows[0].ErrorMessage;
            plan.CanConfirm = false;
            return;
        }

        plan.ValidationMessage = string.Empty;
        plan.CanConfirm = plan.Rows.Count > 0 &&
                          plan.Rows.All(row => !string.IsNullOrWhiteSpace(row.SelectedDownloadFolder) && Directory.Exists(row.SelectedDownloadFolder));
    }

    private void ValidateSeasonConsistency(TorrentAddDiskPlan plan)
    {
        var seasonGroups = plan.Rows
            .Where(row => row.SeasonNumber is not null && row.TargetKind != MediaKind.Movie)
            .GroupBy(row => row.GroupKey)
            .Where(group => group.Key is not null);

        foreach (var group in seasonGroups)
        {
            var folders = group
                .Select(row => row.SelectedDownloadFolder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (folders.Count <= 1)
            {
                continue;
            }

            var seasonLabel = group.First().GroupTitle;
            var message = $"{seasonLabel} must use the same drive.";
            plan.ValidationMessage = message;
            plan.CanConfirm = false;

            foreach (var row in group)
            {
                row.HasError = true;
                row.ErrorMessage = message;
            }
        }
    }

    private static void ValidateFolderAvailability(TorrentAddDiskPlan plan)
    {
        foreach (var row in plan.Rows)
        {
            if (row.HasError)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.SelectedDownloadFolder))
            {
                row.HasError = true;
                row.ErrorMessage = "Select a download folder.";
                continue;
            }

            if (!Directory.Exists(row.SelectedDownloadFolder))
            {
                row.HasError = true;
                row.ErrorMessage = $"Folder does not exist: {row.SelectedDownloadFolder}";
            }
        }
    }

    private static void MarkOverflowRows(TorrentAddDiskPlan plan)
    {
        foreach (var row in plan.Rows)
        {
            if (row.HasError)
            {
                continue;
            }

            var drive = FindDriveForFolder(plan, row.SelectedDownloadFolder);
            if (drive?.IsOverflow == true)
            {
                row.HasError = true;
                row.ErrorMessage = $"Not enough space on {drive.DriveRoot}";
            }
        }
    }

    private static TorrentAddDiskDriveSummary? FindDriveForFolder(TorrentAddDiskPlan plan, string folder)
    {
        var driveRoot = Path.GetPathRoot(folder);
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            return null;
        }

        return plan.Drives.FirstOrDefault(drive => string.Equals(drive.DriveRoot, driveRoot, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryAssignPreferredFolder(
        AssignmentGroup group,
        IReadOnlyDictionary<string, long> driveFree,
        IReadOnlyList<string> folderOptions)
    {
        if (string.IsNullOrWhiteSpace(group.PreferredFolder) ||
            !folderOptions.Contains(group.PreferredFolder, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var driveRoot = Path.GetPathRoot(group.PreferredFolder);
        if (string.IsNullOrWhiteSpace(driveRoot) || !driveFree.TryGetValue(driveRoot, out var freeBytes))
        {
            return null;
        }

        return freeBytes >= group.SizeBytes ? group.PreferredFolder : null;
    }

    private static string? TryAssignBestFitFolder(
        long sizeBytes,
        Dictionary<string, long> driveFree,
        IReadOnlyList<string> folderOptions)
    {
        var candidates = folderOptions
            .Select(folder =>
            {
                var driveRoot = Path.GetPathRoot(folder);
                if (string.IsNullOrWhiteSpace(driveRoot) || !driveFree.TryGetValue(driveRoot, out var freeBytes))
                {
                    return (Folder: folder, FreeBytes: -1L);
                }

                return (Folder: folder, FreeBytes: freeBytes);
            })
            .Where(candidate => candidate.FreeBytes >= sizeBytes)
            .OrderByDescending(candidate => candidate.FreeBytes)
            .ToList();

        return candidates.FirstOrDefault().Folder;
    }

    private static string? GetPreferredFolder(IReadOnlyList<TorrentAddDiskRow> rows)
    {
        return rows
            .Select(row => row.DefaultDownloadFolder)
            .FirstOrDefault(folder => !string.IsNullOrWhiteSpace(folder));
    }

    private string GetDefaultDownloadFolder()
    {
        return FirstNonEmpty(
            _settingsService.Current.AutoTorrent.DownloadFolder,
            _settingsService.Current.SourceFolders.FirstOrDefault()) ?? string.Empty;
    }

    private static long GetPlanningSize(long fileSizeBytes)
    {
        return fileSizeBytes > 0 ? fileSizeBytes : UnknownSizeBufferBytes;
    }

    private static string? BuildGroupKey(TorrentCartOrder order)
    {
        if (order.TargetKind == MediaKind.Movie || order.SeasonNumber is null)
        {
            return $"order:{order.Id}";
        }

        return $"show:{order.MediaId}:season:{order.SeasonNumber.Value}";
    }

    private static string BuildGroupTitle(TorrentCartOrder order)
    {
        if (order.TargetKind == MediaKind.Movie || order.SeasonNumber is null)
        {
            return order.Title;
        }

        return $"{ExtractShowTitle(order.Title)} — Season {order.SeasonNumber.Value}";
    }

    private static string ExtractShowTitle(string title)
    {
        var separatorIndex = title.IndexOf(" — ", StringComparison.Ordinal);
        return separatorIndex > 0 ? title[..separatorIndex] : title;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private sealed class AssignmentGroup
    {
        public string Key { get; init; } = string.Empty;

        public List<TorrentAddDiskRow> Rows { get; init; } = [];

        public long SizeBytes { get; init; }

        public string? PreferredFolder { get; init; }
    }
}
