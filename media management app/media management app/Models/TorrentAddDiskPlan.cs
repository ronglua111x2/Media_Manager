using media_management_app.Common;

namespace media_management_app.Models;

public sealed class TorrentAddDiskPlan
{
    public List<TorrentAddDiskRow> Rows { get; set; } = [];

    public List<TorrentAddDiskDriveSummary> Drives { get; set; } = [];

    public List<string> FolderOptions { get; set; } = [];

    public long TotalSizeBytes { get; set; }

    public bool CanConfirm { get; set; }

    public string ValidationMessage { get; set; } = string.Empty;
}

public sealed class TorrentAddDiskRow
{
    public long OrderId { get; set; }

    public MediaKind TargetKind { get; set; }

    public long MediaId { get; set; }

    public int? SeasonNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public string CandidateName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public long PlanningSizeBytes { get; set; }

    public string DefaultDownloadFolder { get; set; } = string.Empty;

    public string SelectedDownloadFolder { get; set; } = string.Empty;

    public string? GroupKey { get; set; }

    public string GroupTitle { get; set; } = string.Empty;

    public bool HasError { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;
}

public sealed class TorrentAddDiskDriveSummary
{
    public string DriveRoot { get; set; } = string.Empty;

    public string Folder { get; set; } = string.Empty;

    public long TotalBytes { get; set; }

    public long FreeBytes { get; set; }

    public long AssignedBytes { get; set; }

    public long RemainingBytes { get; set; }

    public bool IsOverflow { get; set; }

    public bool IsLowSpace { get; set; }
}
