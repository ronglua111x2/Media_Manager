using System.Net.Http;
using media_management_app.Models;
using media_management_app.Services.Backup;
using media_management_app.ViewModels;

namespace media_management_app.Services;

public sealed class DeviceStatusService : IDeviceStatusService
{
    private readonly ISettingsService _settingsService;
    private readonly IQbittorrentClient _qbittorrentClient;
    private readonly IWarpCliService _warpCliService;
    private readonly IJellyfinClient _jellyfinClient;
    private readonly IGoogleDriveClient _googleDriveClient;
    private readonly IBackupService _backupService;
    private readonly IOperationProgressService _progressService;
    private readonly IAppLogger _logger;

    public DeviceStatusService(
        ISettingsService settingsService,
        IQbittorrentClient qbittorrentClient,
        IWarpCliService warpCliService,
        IJellyfinClient jellyfinClient,
        IGoogleDriveClient googleDriveClient,
        IBackupService backupService,
        IOperationProgressService progressService,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _qbittorrentClient = qbittorrentClient;
        _warpCliService = warpCliService;
        _jellyfinClient = jellyfinClient;
        _googleDriveClient = googleDriveClient;
        _backupService = backupService;
        _progressService = progressService;
        _logger = logger;
        _progressService.ProgressChanged += (_, _) => RefreshJobOnly();
        _backupService.RunStateChanged += (_, _) => RefreshBackupOnly();
        _warpCliService.ConnectionChanged += OnWarpConnectionChanged;
    }

    public event EventHandler? StatusChanged;

    public DeviceStatusSnapshot Current { get; private set; } = new();

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return RefreshCoreAsync(probeWarpCli: true, cancellationToken);
    }

    public void RefreshWarpOnly()
    {
        Current = new DeviceStatusSnapshot
        {
            DriveStatuses = Current.DriveStatuses,
            HasLowSpace = Current.HasLowSpace,
            Qbittorrent = Current.Qbittorrent,
            Warp = BuildWarpStatusFromCache(),
            Jellyfin = Current.Jellyfin,
            GoogleDrive = Current.GoogleDrive,
            IsBackupRunning = Current.IsBackupRunning,
            JobStatus = Current.JobStatus,
            IsJobActive = Current.IsJobActive
        };
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWarpConnectionChanged(object? sender, WarpConnectionChangedEventArgs e) => RefreshWarpOnly();

    private async Task RefreshCoreAsync(bool probeWarpCli, CancellationToken cancellationToken)
    {
        _logger.Info(
            "Dependency status check started (qBittorrent, WARP, Jellyfin).",
            Common.LogTarget.File);

        var drives = GetStorageStatuses();
        var qbittorrentTask = GetQbittorrentDependencyAsync(cancellationToken);
        var warpTask = probeWarpCli
            ? GetWarpDependencyAsync(cancellationToken)
            : Task.FromResult(BuildWarpStatusFromCache());
        var jellyfinTask = GetJellyfinDependencyAsync(cancellationToken);
        await Task.WhenAll(qbittorrentTask, warpTask, jellyfinTask);

        var qbittorrent = await qbittorrentTask;
        var warp = await warpTask;
        var jellyfin = await jellyfinTask;

        Current = new DeviceStatusSnapshot
        {
            DriveStatuses = drives,
            HasLowSpace = drives.Any(status => status.IsLowSpace),
            Qbittorrent = qbittorrent,
            Warp = warp,
            Jellyfin = jellyfin,
            GoogleDrive = GetGoogleDriveDependency(),
            IsBackupRunning = _backupService.IsRunning,
            JobStatus = GetJobStatus(),
            IsJobActive = _progressService.IsActive
        };

        _logger.Info(
            $"Dependency status result: qBittorrent={qbittorrent.StatusText}, WARP={warp.StatusText}, Jellyfin={jellyfin.StatusText}.",
            Common.LogTarget.File);
        LogDependencyDetailIfNeeded(qbittorrent);
        LogDependencyDetailIfNeeded(warp);
        LogDependencyDetailIfNeeded(jellyfin);

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LogDependencyDetailIfNeeded(DependencyStatusInfo status)
    {
        if (status.IsOk)
        {
            return;
        }

        // Expected non-ok states (disconnected / not set up) are already in the result line.
        var text = status.StatusText;
        if (string.Equals(text, "disconnected", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "not configured", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "not installed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "connecting", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "disconnecting", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.Info(
            $"Dependency status detail: {status.Name} — {status.Detail}",
            Common.LogTarget.File);
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
            GoogleDrive = Current.GoogleDrive,
            IsBackupRunning = Current.IsBackupRunning,
            JobStatus = GetJobStatus(),
            IsJobActive = _progressService.IsActive
        };
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshBackupOnly()
    {
        Current = new DeviceStatusSnapshot
        {
            DriveStatuses = Current.DriveStatuses,
            HasLowSpace = Current.HasLowSpace,
            Qbittorrent = Current.Qbittorrent,
            Warp = Current.Warp,
            Jellyfin = Current.Jellyfin,
            GoogleDrive = GetGoogleDriveDependency(),
            IsBackupRunning = _backupService.IsRunning,
            JobStatus = Current.JobStatus,
            IsJobActive = Current.IsJobActive
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
            return BuildWarpNotInstalled();
        }

        try
        {
            await _warpCliService.IsConnectedAsync(cancellationToken);
            return BuildWarpStatusFromCache();
        }
        catch (Exception ex)
        {
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

    private DependencyStatusInfo BuildWarpStatusFromCache()
    {
        if (!_warpCliService.IsAvailable)
        {
            return BuildWarpNotInstalled();
        }

        if (_warpCliService.InFlight)
        {
            var connecting = !_warpCliService.LastKnownConnected;
            return new DependencyStatusInfo
            {
                Name = "WARP",
                IsOk = _warpCliService.LastKnownConnected,
                IsConfigured = true,
                StatusText = connecting ? "connecting" : "disconnecting",
                Detail = connecting ? "Connecting…" : "Disconnecting…"
            };
        }

        var connected = _warpCliService.LastKnownConnected;
        var leaseText = WarpLeaseReasonText.Describe(_warpCliService.ActiveLeases);
        string detail;
        if (connected)
        {
            detail = string.IsNullOrEmpty(leaseText)
                ? "Connected. Click to disconnect."
                : $"Connected ({leaseText}). Click to disconnect.";
        }
        else
        {
            detail = "Disconnected. Click to connect.";
        }

        return new DependencyStatusInfo
        {
            Name = "WARP",
            IsOk = connected,
            IsConfigured = true,
            StatusText = connected ? "connected" : "disconnected",
            Detail = detail
        };
    }

    private DependencyStatusInfo BuildWarpNotInstalled() =>
        new()
        {
            Name = "WARP",
            IsOk = false,
            IsConfigured = false,
            StatusText = "not installed",
            Detail = $"WARP CLI not found at {_warpCliService.ResolvedExecutablePath}"
        };

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

    private DependencyStatusInfo GetGoogleDriveDependency()
    {
        if (_backupService.IsRunning)
        {
            return new DependencyStatusInfo
            {
                Name = "Google Drive",
                IsOk = true,
                IsConfigured = true,
                StatusText = "backing up",
                Detail = "Backup in progress..."
            };
        }

        if (!File.Exists(_googleDriveClient.CredentialsFilePath))
        {
            return new DependencyStatusInfo
            {
                Name = "Google Drive",
                IsOk = false,
                IsConfigured = false,
                StatusText = "not configured",
                Detail = "Google Drive credentials.json is not configured"
            };
        }

        var connected = _googleDriveClient.HasStoredCredential;
        return new DependencyStatusInfo
        {
            Name = "Google Drive",
            IsOk = connected,
            IsConfigured = true,
            StatusText = connected ? "connected" : "not connected",
            Detail = connected ? "Google Drive is connected" : "Google Drive is not connected yet"
        };
    }

    private string GetJobStatus() =>
        _progressService.IsActive ? _progressService.Message : "Idle";
}
