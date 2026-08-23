namespace media_management_app.Models;

public sealed class QbittorrentProcessRestartSettings
{
    public const string DefaultExecutablePath = @"C:\Program Files\qBittorrent\qbittorrent.exe";

    public const int DefaultGracefulShutdownSeconds = 15;
    public const int MinGracefulShutdownSeconds = 5;
    public const int MaxGracefulShutdownSeconds = 120;

    public const int DefaultCooldownMinutes = 15;
    public const int MinCooldownMinutes = 5;
    public const int MaxCooldownMinutes = 120;

    public const int DefaultMaxRestartsPerHour = 2;
    public const int MinMaxRestartsPerHour = 1;
    public const int MaxMaxRestartsPerHour = 6;

    public const int DefaultWebUiReadyTimeoutSeconds = 60;
    public const int MinWebUiReadyTimeoutSeconds = 15;
    public const int MaxWebUiReadyTimeoutSeconds = 180;

    /// <summary>
    /// Opt-in. When true, Auto-Track may restart qbittorrent.exe only for WebUI bind-fail
    /// (connection refused/timeout while the process is running). Never for auth/URL errors.
    /// </summary>
    public bool Enabled { get; set; }

    public string ExecutablePath { get; set; } = DefaultExecutablePath;

    public int GracefulShutdownSeconds { get; set; } = DefaultGracefulShutdownSeconds;

    public int CooldownMinutes { get; set; } = DefaultCooldownMinutes;

    public int MaxRestartsPerHour { get; set; } = DefaultMaxRestartsPerHour;

    public int WebUiReadyTimeoutSeconds { get; set; } = DefaultWebUiReadyTimeoutSeconds;
}
