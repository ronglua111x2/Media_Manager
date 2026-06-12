using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class TorrentAddDiskDriveSummaryViewModel : ObservableObject
{
    public string DriveRoot { get; init; } = string.Empty;

    public string FolderDisplay { get; init; } = string.Empty;

    [ObservableProperty]
    private long totalBytes;

    [ObservableProperty]
    private long freeBytes;

    [ObservableProperty]
    private long assignedBytes;

    [ObservableProperty]
    private long remainingBytes;

    [ObservableProperty]
    private bool isOverflow;

    [ObservableProperty]
    private bool isLowSpace;

    public string FreeDisplay => StorageStatusViewModel.FormatSize(FreeBytes);

    public string TotalDisplay => StorageStatusViewModel.FormatSize(TotalBytes);

    public string AssignedDisplay => AssignedBytes <= 0 ? string.Empty : $"+{StorageStatusViewModel.FormatSize(AssignedBytes)} assigned";

    public bool HasAssignedBytes => AssignedBytes > 0;

    public double UsedFraction
    {
        get
        {
            if (TotalBytes <= 0)
            {
                return 0;
            }

            var usedBytes = Math.Max(0, TotalBytes - RemainingBytes);
            return Math.Min(1, (double)usedBytes / TotalBytes);
        }
    }

    public string ProgressBrushKey => IsOverflow ? "Danger" : IsLowSpace || RemainingBytes < TotalBytes * 0.2 ? "Warning" : "Accent";

    partial void OnAssignedBytesChanged(long value)
    {
        OnPropertyChanged(nameof(AssignedDisplay));
        OnPropertyChanged(nameof(HasAssignedBytes));
        OnPropertyChanged(nameof(UsedFraction));
        OnPropertyChanged(nameof(ProgressBrushKey));
    }

    partial void OnRemainingBytesChanged(long value)
    {
        OnPropertyChanged(nameof(UsedFraction));
        OnPropertyChanged(nameof(ProgressBrushKey));
    }

    partial void OnIsOverflowChanged(bool value) => OnPropertyChanged(nameof(ProgressBrushKey));

    partial void OnIsLowSpaceChanged(bool value) => OnPropertyChanged(nameof(ProgressBrushKey));

    public void Apply(TorrentAddDiskDriveSummary summary)
    {
        AssignedBytes = summary.AssignedBytes;
        RemainingBytes = summary.RemainingBytes;
        IsOverflow = summary.IsOverflow;
        IsLowSpace = summary.IsLowSpace;
    }
}

public sealed partial class TorrentAddDiskFolderOptionViewModel : ObservableObject
{
    public string Folder { get; init; } = string.Empty;

    public string DriveRoot { get; init; } = string.Empty;

    public string BaseLabel { get; init; } = string.Empty;

    [ObservableProperty]
    private string freeDisplay = string.Empty;

    public string DisplayText => string.IsNullOrWhiteSpace(FreeDisplay)
        ? BaseLabel
        : $"{BaseLabel} ({FreeDisplay} free)";
}

public sealed partial class TorrentAddDiskRowViewModel : ObservableObject
{
    private readonly Action _onFolderChanged;

    public TorrentAddDiskRowViewModel(Action onFolderChanged)
    {
        _onFolderChanged = onFolderChanged;
    }

    public long OrderId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string CandidateName { get; init; } = string.Empty;

    public string FileSizeDisplay { get; init; } = string.Empty;

    public string? GroupKey { get; init; }

    public ObservableCollection<TorrentAddDiskFolderOptionViewModel> FolderOptions { get; } = [];

    [ObservableProperty]
    private string selectedDownloadFolder = string.Empty;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    partial void OnSelectedDownloadFolderChanged(string value) => _onFolderChanged();
}

public sealed class TorrentAddDiskGroupViewModel
{
    public string Title { get; init; } = string.Empty;

    public bool IsSeasonGroup { get; init; }

    public ObservableCollection<TorrentAddDiskRowViewModel> Rows { get; } = [];
}
