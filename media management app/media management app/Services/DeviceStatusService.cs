using System.Net.Http;
using media_management_app.Models;
using media_management_app.ViewModels;

namespace media_management_app.Services;

public sealed class DeviceStatusService : IDeviceStatusService
{
    private readonly ISettingsService _settingsService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IFetchJobService _fetchJobService;
    private readonly IOperationProgressService _progressService;
    private readonly IAppLogger _logger;

    public DeviceStatusService(
        ISettingsService settingsService,
        IQbittorrentClient qbittorrentClient,
        IFetchJobService fetchJobService,
        IOperationProgressService progressService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _qbittorrentClient = qbittorrentClient;
        _fetchJobService = fetchJobService;
        _progressService = progressService;
        _logger = logger;
        _fetchJobService.JobsChanged += (_, _) => RefreshJobOnly();
        _progressService.ProgressChanged += (_, _) => RefreshJobOnly();
    }

    public event EventHandler? StatusChanged;

    public DeviceStatusSnapshot Current { get; private set; } = new();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var storage = GetStorageStatus();
        var qbittorrentStatus = "qBittorrent: offline";
        var isConnected = false;
        try
        {
            var torrents = await _qbittorrentClient.GetTorrentsAsync(cancellationToken);
            qbittorrentStatus = $"qBittorrent: connected ({torrents.Count} torrents)";
            isConnected = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            qbittorrentStatus = $"qBittorrent: {ex.Message}";
            _logger.Debug($"Device status qBittorrent check failed: {ex.Message}", Common.LogTarget.File | Common.LogTarget.Console);
        }

        Current = new DeviceStatusSnapshot
        {
            StorageSummary = storage.Summary,
            HasLowSpace = storage.HasLowSpace,
            QbittorrentStatus = qbittorrentStatus,
            IsQbittorrentConnected = isConnected,
            JobStatus = GetJobStatus()
        };
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshJobOnly()
    {
        Current = new DeviceStatusSnapshot
        {
            StorageSummary = Current.StorageSummary,
            HasLowSpace = Current.HasLowSpace,
            QbittorrentStatus = Current.QbittorrentStatus,
            IsQbittorrentConnected = Current.IsQbittorrentConnected,
            JobStatus = GetJobStatus()
        };
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private (string Summary, bool HasLowSpace) GetStorageStatus()
    {
        var folders = GetKnownFolders().ToList();
        if (folders.Count == 0)
        {
            return ("Storage: no folders configured", false);
        }

        var statuses = folders
            .Select(GetDriveStatus)
            .Where(status => status is not null)
            .Select(status => status!)
            .GroupBy(status => status.DriveRoot, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(status => status.FreeBytes).First())
            .ToList();
        if (statuses.Count == 0)
        {
            return ("Storage: unavailable", false);
        }

        var lowest = statuses.OrderBy(status => status.FreeBytes).First();
        return ($"Storage: {lowest.DriveRoot} {lowest.FreeDisplay} free ({lowest.FreePercentDisplay})", statuses.Any(status => status.IsLowSpace));
    }

    private StorageStatusViewModel? GetDriveStatus(string folder)
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
                DriveRoot = root,
                Folder = folder,
                TotalBytes = drive.TotalSize,
                FreeBytes = drive.AvailableFreeSpace
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.Debug($"Could not read drive status for '{folder}': {ex.Message}", Common.LogTarget.File | Common.LogTarget.Console);
            return null;
        }
    }

    private IEnumerable<string> GetKnownFolders()
    {
        var settings = _settingsService.Current;
        if (!string.IsNullOrWhiteSpace(settings.AutoTorrent.DownloadFolder))
        {
            yield return settings.AutoTorrent.DownloadFolder;
        }

        foreach (var folder in settings.AutoTorrent.DownloadFolders)
        {
            yield return folder;
        }

        foreach (var folder in settings.SourceFolders)
        {
            yield return folder;
        }
    }

    private string GetJobStatus()
    {
        if (_progressService.IsActive)
        {
            return $"Jobs: {_progressService.Message}";
        }

        var activeJobs = _fetchJobService.GetJobs()
            .Count(job => job.Status is Common.FetchJobStatus.Pending or Common.FetchJobStatus.Running);
        return activeJobs == 0 ? "Jobs: idle" : $"Jobs: {activeJobs} active";
    }
}
