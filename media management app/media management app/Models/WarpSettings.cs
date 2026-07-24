namespace media_management_app.Models;

public sealed class WarpSettings
{
    public bool Enabled { get; set; } = true;

    public string? ExecutablePath { get; set; }

    public int ConnectTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When true, Auto-Track TMDB refresh self-recovers on SSL/TLS errors by
    /// connecting WARP for the rest of that cycle, then disconnecting.
    /// Does not apply to Library or other non-auto-track TMDB calls.
    /// </summary>
    public bool AutoRecoverOnSsl { get; set; } = true;
}
