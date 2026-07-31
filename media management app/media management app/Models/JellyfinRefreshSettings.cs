namespace media_management_app.Models;

public sealed class JellyfinRefreshSettings
{
    public const int DefaultWarpHoldSecondsAfterNotify = 120;
    public const int MinWarpHoldSecondsAfterNotify = 15;
    public const int MaxWarpHoldSecondsAfterNotify = 600;

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
}
