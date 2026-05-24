namespace media_management_app.Models;

public sealed class AddedTorrentResult
{
    public string Hash { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public string SavePath { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;
}

