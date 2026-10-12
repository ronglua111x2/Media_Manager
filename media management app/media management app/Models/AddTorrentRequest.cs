namespace media_management_app.Models;

public sealed class AddTorrentRequest
{
    public string Url { get; init; } = string.Empty;

    public string PluginName { get; init; } = string.Empty;

    public string SavePath { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Tags { get; init; } = "media-manager";

    public bool Paused { get; init; }
}
