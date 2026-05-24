namespace media_management_app.Models;

public sealed class TorrentSearchRequest
{
    public string Query { get; init; } = string.Empty;

    public string Plugins { get; init; } = "enabled";

    public string Category { get; init; } = "all";

    public int Limit { get; init; } = 100;
}

