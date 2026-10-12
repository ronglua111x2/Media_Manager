namespace media_management_app.Models;

public enum QbittorrentWebUiProbeStatus
{
    Ok = 0,
    Unreachable = 1,
    AuthFailed = 2,
    InvalidUrl = 3
}

public sealed class QbittorrentWebUiProbeResult
{
    public required QbittorrentWebUiProbeStatus Status { get; init; }

    public string? Version { get; init; }

    public string? Detail { get; init; }

    public bool IsOk => Status == QbittorrentWebUiProbeStatus.Ok;

    public bool IsUnreachable => Status == QbittorrentWebUiProbeStatus.Unreachable;

    public static QbittorrentWebUiProbeResult Ok(string? version) => new()
    {
        Status = QbittorrentWebUiProbeStatus.Ok,
        Version = version,
        Detail = string.IsNullOrWhiteSpace(version) ? "qBittorrent WebUI reachable" : $"qBittorrent {version}"
    };

    public static QbittorrentWebUiProbeResult Unreachable(string detail) => new()
    {
        Status = QbittorrentWebUiProbeStatus.Unreachable,
        Detail = detail
    };

    public static QbittorrentWebUiProbeResult AuthFailed(string detail) => new()
    {
        Status = QbittorrentWebUiProbeStatus.AuthFailed,
        Detail = detail
    };

    public static QbittorrentWebUiProbeResult InvalidUrl(string detail) => new()
    {
        Status = QbittorrentWebUiProbeStatus.InvalidUrl,
        Detail = detail
    };
}
