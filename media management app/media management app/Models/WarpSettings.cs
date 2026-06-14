namespace media_management_app.Models;

public sealed class WarpSettings
{
    public bool Enabled { get; set; } = true;

    public string? ExecutablePath { get; set; }

    public int ConnectTimeoutSeconds { get; set; } = 30;
}
