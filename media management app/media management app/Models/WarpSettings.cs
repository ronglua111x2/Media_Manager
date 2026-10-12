namespace media_management_app.Models;

public sealed class WarpSettings
{
    public bool Enabled { get; set; } = true;

    public string? ExecutablePath { get; set; }

    public int ConnectTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When true, Auto-Track TMDB refresh and Jellyfin path-refresh pre-probe
    /// self-recover on SSL/TLS errors by connecting WARP for that operation,
    /// then disconnecting if this app owned the connect.
    /// Does not apply to Library or other non-auto-track TMDB calls.
    /// </summary>
    public bool AutoRecoverOnSsl { get; set; } = true;

    /// <summary>
    /// When true, the status pill asks before disconnecting WARP if Auto-Track
    /// currently holds a pipeline lease (TMDB recover, hunt, or Jellyfin hold).
    /// </summary>
    public bool ConfirmDisconnectDuringAutoTrack { get; set; } = true;
}
