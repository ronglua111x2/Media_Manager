using media_management_app.ViewModels;

namespace media_management_app.Services;

public sealed class DownloadFolderOption
{
    public string Folder { get; init; } = string.Empty;

    public string DriveRoot { get; init; } = string.Empty;

    public string DisplayLabel { get; init; } = string.Empty;
}

public interface IDownloadFolderCatalogService
{
    IReadOnlyList<string> GetKnownDownloadFolders();

    IReadOnlyList<DownloadFolderOption> GetDownloadFolderOptions();

    IReadOnlyList<StorageStatusViewModel> GetDriveStatuses();
}
