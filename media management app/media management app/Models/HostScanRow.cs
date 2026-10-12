namespace media_management_app.Models;

public enum HostScanComponentId
{
    Windows,
    App,
    WebView2,
    Qbittorrent,
    Warp,
    Jellyfin,
    Tmdb,
    Gemini,
    Drive
}

public enum HostScanCheckStatus
{
    NotApplicable,
    Ok,
    Missing,
    NotConfigured,
    Failed,
    Warning
}

public enum HostScanSeverity
{
    Ok,
    Info,
    Warning,
    Error
}

public sealed class HostScanOverrides
{
    public string? QbittorrentExecutablePath { get; set; }

    public string? WarpCliPath { get; set; }
}

public sealed class HostScanRow
{
    public required HostScanComponentId Id { get; init; }

    public required string Title { get; init; }

    public bool CanBrowse { get; init; }

    public string? Path { get; init; }

    public string? DetectedVersion { get; init; }

    public required string RequiredLabel { get; init; }

    public HostScanCheckStatus Installed { get; init; }

    public HostScanCheckStatus Running { get; init; }

    public HostScanCheckStatus Usable { get; init; }

    public HostScanSeverity Severity { get; init; }

    public string Note { get; init; } = string.Empty;

    public string LogLine =>
        $"Host scan: {Title} installed={Installed} running={Running} usable={Usable} version={DetectedVersion ?? "—"} path={Path ?? "—"} note={Note}";
}
