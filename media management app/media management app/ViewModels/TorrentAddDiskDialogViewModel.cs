using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class TorrentAddDiskDialogViewModel : ObservableObject
{
    private readonly ITorrentAddDiskAssignmentService _assignmentService;
    private readonly IDownloadFolderCatalogService _downloadFolderCatalogService;
    private TorrentAddDiskPlan _plan;

    public TorrentAddDiskDialogViewModel(
        TorrentAddDiskPlan plan,
        ITorrentAddDiskAssignmentService assignmentService,
        IDownloadFolderCatalogService downloadFolderCatalogService)
    {
        _plan = plan;
        _assignmentService = assignmentService;
        _downloadFolderCatalogService = downloadFolderCatalogService;
        BuildViewModels();
    }

    public ObservableCollection<TorrentAddDiskDriveSummaryViewModel> Drives { get; } = [];

    public ObservableCollection<TorrentAddDiskGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    private string totalSizeDisplay = string.Empty;

    [ObservableProperty]
    private int torrentCount;

    [ObservableProperty]
    private bool hasValidationError;

    [ObservableProperty]
    private string validationMessage = string.Empty;

    [ObservableProperty]
    private bool canConfirm;

    [ObservableProperty]
    private string confirmButtonText = "Add torrents";

    public TorrentAddDiskPlan Plan => _plan;

    [RelayCommand]
    private void AutoDistribute()
    {
        _assignmentService.AutoAssign(_plan);
        RefreshFromPlan();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm(Window? window)
    {
        if (window is null)
        {
            return;
        }

        window.DialogResult = true;
        window.Close();
    }

    [RelayCommand]
    private void Cancel(Window? window)
    {
        if (window is null)
        {
            return;
        }

        window.DialogResult = false;
        window.Close();
    }

    private void BuildViewModels()
    {
        Drives.Clear();
        foreach (var drive in _plan.Drives)
        {
            Drives.Add(new TorrentAddDiskDriveSummaryViewModel
            {
                DriveRoot = drive.DriveRoot,
                FolderDisplay = drive.Folder,
                TotalBytes = drive.TotalBytes,
                FreeBytes = drive.FreeBytes,
                AssignedBytes = drive.AssignedBytes,
                RemainingBytes = drive.RemainingBytes,
                IsOverflow = drive.IsOverflow,
                IsLowSpace = drive.IsLowSpace
            });
        }

        Groups.Clear();
        var folderOptions = BuildFolderOptions();
        foreach (var group in _plan.Rows.GroupBy(row => row.GroupKey ?? $"order:{row.OrderId}"))
        {
            var first = group.First();
            var groupVm = new TorrentAddDiskGroupViewModel
            {
                Title = first.TargetKind != Common.MediaKind.Movie && first.SeasonNumber is not null
                    ? $"{first.GroupTitle} ({group.Count()} episode{(group.Count() == 1 ? string.Empty : "s")})"
                    : first.GroupTitle,
                IsSeasonGroup = first.TargetKind != Common.MediaKind.Movie && first.SeasonNumber is not null
            };

            foreach (var row in group)
            {
                groupVm.Rows.Add(new TorrentAddDiskRowViewModel(RecalculateFromUi)
                {
                    OrderId = row.OrderId,
                    Title = row.Title,
                    CandidateName = row.CandidateName,
                    FileSizeDisplay = row.FileSizeBytes > 0
                        ? StorageStatusViewModel.FormatSize(row.FileSizeBytes)
                        : "unknown",
                    GroupKey = row.GroupKey,
                    SelectedDownloadFolder = row.SelectedDownloadFolder
                });
            }

            foreach (var rowVm in groupVm.Rows)
            {
                foreach (var option in folderOptions)
                {
                    rowVm.FolderOptions.Add(option);
                }
            }

            Groups.Add(groupVm);
        }

        RefreshSummary();
    }

    private List<TorrentAddDiskFolderOptionViewModel> BuildFolderOptions()
    {
        var driveFreeLookup = _plan.Drives.ToDictionary(
            drive => drive.DriveRoot,
            drive => drive.FreeBytes,
            StringComparer.OrdinalIgnoreCase);

        return _downloadFolderCatalogService.GetDownloadFolderOptions()
            .Select(option =>
            {
                var freeBytes = driveFreeLookup.TryGetValue(option.DriveRoot, out var free) ? free : 0L;
                return new TorrentAddDiskFolderOptionViewModel
                {
                    Folder = option.Folder,
                    DriveRoot = option.DriveRoot,
                    BaseLabel = option.DisplayLabel,
                    FreeDisplay = StorageStatusViewModel.FormatSize(freeBytes)
                };
            })
            .ToList();
    }

    private void RecalculateFromUi()
    {
        TorrentAddDiskRowViewModel? changedRow = null;
        foreach (var group in Groups)
        {
            foreach (var rowVm in group.Rows)
            {
                var row = _plan.Rows.FirstOrDefault(item => item.OrderId == rowVm.OrderId);
                if (row is null)
                {
                    continue;
                }

                if (!string.Equals(row.SelectedDownloadFolder, rowVm.SelectedDownloadFolder, StringComparison.OrdinalIgnoreCase))
                {
                    changedRow ??= rowVm;
                }

                row.SelectedDownloadFolder = rowVm.SelectedDownloadFolder;
            }
        }

        if (changedRow?.GroupKey is not null)
        {
            foreach (var group in Groups)
            {
                foreach (var rowVm in group.Rows.Where(row => string.Equals(row.GroupKey, changedRow.GroupKey, StringComparison.Ordinal)))
                {
                    rowVm.SelectedDownloadFolder = changedRow.SelectedDownloadFolder;
                    var row = _plan.Rows.FirstOrDefault(item => item.OrderId == rowVm.OrderId);
                    if (row is not null)
                    {
                        row.SelectedDownloadFolder = changedRow.SelectedDownloadFolder;
                    }
                }
            }
        }

        _assignmentService.Recalculate(_plan);
        RefreshFromPlan();
    }

    private void RefreshFromPlan()
    {
        foreach (var driveVm in Drives)
        {
            var drive = _plan.Drives.FirstOrDefault(item => string.Equals(item.DriveRoot, driveVm.DriveRoot, StringComparison.OrdinalIgnoreCase));
            if (drive is not null)
            {
                driveVm.Apply(drive);
            }
        }

        foreach (var group in Groups)
        {
            foreach (var rowVm in group.Rows)
            {
                var row = _plan.Rows.FirstOrDefault(item => item.OrderId == rowVm.OrderId);
                if (row is null)
                {
                    continue;
                }

                rowVm.HasError = row.HasError;
                rowVm.ErrorMessage = row.ErrorMessage;
            }
        }

        RefreshSummary();
    }

    private void RefreshSummary()
    {
        TorrentCount = _plan.Rows.Count;
        TotalSizeDisplay = $"{TorrentCount} torrent{(TorrentCount == 1 ? string.Empty : "s")} · {StorageStatusViewModel.FormatSize(_plan.TotalSizeBytes)}";
        ConfirmButtonText = $"Add {TorrentCount} torrent{(TorrentCount == 1 ? string.Empty : "s")}";
        HasValidationError = !string.IsNullOrWhiteSpace(_plan.ValidationMessage);
        ValidationMessage = _plan.ValidationMessage;
        CanConfirm = _plan.CanConfirm;
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}
