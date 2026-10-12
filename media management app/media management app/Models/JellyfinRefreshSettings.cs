namespace media_management_app.Models;

public sealed class JellyfinRefreshSettings
{
    public const int DefaultWarpHoldSecondsAfterNotify = 120;
    public const int MinWarpHoldSecondsAfterNotify = 15;
    public const int MaxWarpHoldSecondsAfterNotify = 600;

    public const int DefaultLogQuietSecondsAfterRefresh = 30;
    public const int MinLogQuietSecondsAfterRefresh = 10;
    public const int MaxLogQuietSecondsAfterRefresh = 120;

    /// <summary>
    /// When true, Auto-Track symlink create/repair (and SyncNow) notifies Jellyfin
    /// of new paths via Library/Media/Updated.
    /// </summary>
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://127.0.0.1:8096";

    public string? ApiKey { get; set; }

    /// <summary>
    /// Seconds to keep an owned WARP session after Jellyfin accepts path notify,
    /// so LibraryMonitor/TMDB can finish while WARP is still up.
    /// </summary>
    public int WarpHoldSecondsAfterNotify { get; set; } = DefaultWarpHoldSecondsAfterNotify;

    /// <summary>
    /// When true and <see cref="LogPath"/> validates, owned WARP hold may end early
    /// after Jellyfin log shows refresh + quiet period. Invalid path keeps timer-only behavior.
    /// </summary>
    public bool EnableLogEarlyDisconnect { get; set; }

    /// <summary>
    /// Jellyfin log folder (preferred) or a specific <c>.log</c> file path.
    /// </summary>
    public string? LogPath { get; set; }

    /// <summary>
    /// Quiet window after the last matching Jellyfin log line before early WARP disconnect.
    /// </summary>
    public int LogQuietSecondsAfterRefresh { get; set; } = DefaultLogQuietSecondsAfterRefresh;

    /// <summary>
    /// When true, closing the standalone Jellyfin viewer asks for confirmation.
    /// Skipped for background auto-close and app shutdown.
    /// </summary>
    public bool ConfirmCloseViewer { get; set; } = true;

    /// <summary>
    /// When true, the standalone Jellyfin viewer closes (no confirm) when the app
    /// enters background mode.
    /// </summary>
    public bool AutoCloseViewerOnBackground { get; set; } = true;
}
