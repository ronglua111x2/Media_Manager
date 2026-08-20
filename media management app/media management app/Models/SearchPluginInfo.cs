namespace media_management_app.Models;

public sealed class SearchPluginInfo
{
    public string Name { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public string Url { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;
}
