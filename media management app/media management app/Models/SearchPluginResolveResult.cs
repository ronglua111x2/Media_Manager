namespace media_management_app.Models;

public sealed class SearchPluginResolveResult
{
    public string PluginsForApi { get; init; } = "enabled";

    public IReadOnlyList<string> RequestedNames { get; init; } = [];

    public bool SkipSearch { get; init; }

    public IReadOnlyList<string> SkippedNames { get; init; } = [];
}
