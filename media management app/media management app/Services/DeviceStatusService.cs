using System.Net.Http;
using media_management_app.Models;
using media_management_app.ViewModels;

namespace media_management_app.Services;

public sealed class DeviceStatusService : IDeviceStatusService
{
    private readonly ISettingsService _settingsService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IWarpCliService _warpCliService;
    private readonly IJellyfinClient _jellyfinClient;
    private readonly IOperationProgressService _progressService;
    private readonly IAppLogger _logger;

    public DeviceStatusService(
        ISettingsService settingsService,
        IQbittorrentClient qbittorrentClient,
        IWarpCliService warpCliService,
        IJellyfinClient jellyfinClient,
        IOperationProgressService progressService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _qbittorrentClient = qbittorrentClient;
        _warpCliService = warpCliService;
        _jellyfinClient = jellyfinClient;
        _progressService = progressService;
        _logger = logger;
        _progressService.ProgressChanged += (_, _) => RefreshJobOnly();
    }

    public event EventHandler? StatusChanged;

    public DeviceStatusSnapshot Current { get; private set; } = new();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var drives = GetStorageStatuses();
        var qbittorrentTask = GetQbittorrentDependencyAsync(cancellationToken);
        var warpTask = GetWarpDependencyAsync(cancellationToken);
        var jellyfinTask = GetJellyfinDependencyAsync(cancellationToken);
        await Task.WhenAll(qbittorrentTask, warpTask, jellyfinTask);

        Current = new DeviceStatusSnapshot
        {
            DriveStatuses = drives,
            HasLowSpace = drives.Any(status => status.IsLowSpace),
            Qbittorrent = await qbittorrentTask,
            Warp = await warpTask,
            Jellyfin = await jellyfinTask,
            JobStatus = GetJobStatus(),
            IsJobActive = _progressService.IsActive
        };
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshJobOnly()
    {
        Current = new DeviceStatusSnapshot
        {
            DriveStatuses = Current.DriveStatuses,
            HasLowSpace = Current.HasLowSpace,
            Qbittorrent = Current.Qbittorrent,
            Warp = Current.Warp,
            Jellyfin = Current.Jellyfin,
            JobStatus = GetJobStatus(),
            IsJobActive = _progressService.IsActive
        };
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<StorageStatusViewModel> GetStorageStatuses()
    {
        var folders = GetKnownFolders().ToList();
        if (folders.Count == 0)
        {
            return [];
        }

        return folders
            .Select(GetDriveStatus)
            .Where(status => status is not null)
            .Select(status => status!)
            .GroupBy(status => status.DriveRoot, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(status => status.FreeBytes).First())
            .OrderBy(status => status.DriveRoot, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private async Task<DependencyStatusInfo> GetQbittorrentDependencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var version = await _qbittorrentClient.TestConnectionAsync(cancellationToken);
            return new DependencyStatusInfo
            {
                Name = "qBittorrent",
                IsOk = true,
                IsConfigured = true,
                StatusText = "connected",
                Detail = string.IsNullOrWhiteSpace(version)
                    ? "qBittorrent connected"
                    : $"qBittorrent connected ({version})"
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.Debug($"Device status qBittorrent check failed: {ex.Message}", Common.LogTarget.File);
            return new DependencyStatusInfo
            {
                Name = "qBittorrent",
                IsOk = false,
                IsConfigured = true,
                StatusText = "offline",
                Detail = $"qBittorrent offline: {ex.Message}"
            };
        }
    }

    private async Task<DependencyStatusInfo> GetWarpDependencyAsync(CancellationToken cancellationToken)
    {
        if (!_warpCliService.IsAvailable)
        {
            return new DependencyStatusInfo
            {
                Name = "WARP",
                IsOk = false,
                IsConfigured = false,
                StatusText = "not installed",
                Detail = $"WARP CLI not found at {_warpCliService.ResolvedExecutablePath}"
            };
        }

        try
        {
            var connected = await _warpCliService.IsConnectedAsync(cancellationToken);
            return new DependencyStatusInfo
            {
                Name = "WARP",
                IsOk = connected,
                IsConfigured = true,
                StatusText = connected ? "connected" : "disconnected",
                Detail = connected ? "WARP is connected" : "WARP is disconnected"
            };
        }
        catch (Exception ex)
        {
            _logger.Debug($"Device status WARP check failed: {ex.Message}", Common.LogTarget.File);
            return new DependencyStatusInfo
            {
                Name = "WARP",
                IsOk = false,
                IsConfigured = true,
                StatusText = "error",
                Detail = $"WARP check failed: {ex.Message}"
            };
        }
    }

    private async Task<DependencyStatusInfo> GetJellyfinDependencyAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current.AutoTrack?.Jellyfin;
        if (settings is null
            || string.IsNullOrWhiteSpace(settings.BaseUrl)
            || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return new DependencyStatusInfo
            {
                Name = "Jellyfin",
                IsOk = false,
                IsConfigured = false,
                StatusText = "not configured",
                Detail = "Jellyfin Base URL or API key is not configured"
            };
        }

        try
        {
            var message = await _jellyfinClient.TestConnectionAsync(cancellationToken);
            return new DependencyStatusInfo
            {
                Name = "Jellyfin",
                IsOk = true,
                IsConfigured = true,
                StatusText = "available",
                Detail = string.IsNullOrWhiteSpace(message) ? "Jellyfin is available" : message
            };
        }
        catch (Exception ex)
        {
            _logger.Debug($"Device status Jellyfin check failed: {ex.Message}", Common.LogTarget.File);
            return new DependencyStatusInfo
            {
                Name = "Jellyfin",
                IsOk = false,
                IsConfigured = true,
                StatusText = "unavailable",
                Detail = $"Jellyfin unavailable: {ex.Message}"
            };
        }
    }

    private string GetJobStatus() =>
        _progressService.IsActive ? _progressService.Message : "Idle";
}
