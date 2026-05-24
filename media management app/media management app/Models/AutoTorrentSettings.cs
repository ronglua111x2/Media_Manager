namespace media_management_app.Models;

public sealed class AutoTorrentSettings
{
    public string QbittorrentWebUiUrl { get; set; } = "http://localhost:8080";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? DownloadFolder { get; set; }

    public string CategoryName { get; set; } = "AutoTorrent";
}
