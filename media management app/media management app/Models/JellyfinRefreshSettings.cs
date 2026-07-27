namespace media_management_app.Models;

public sealed class JellyfinRefreshSettings
{
    /// <summary>
    /// When true, Auto-Track symlink create/repair (and SyncNow) notifies Jellyfin
    /// of new paths via Library/Media/Updated.
    /// </summary>
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://127.0.0.1:8096";

    public string? ApiKey { get; set; }
}
