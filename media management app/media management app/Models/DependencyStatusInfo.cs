namespace media_management_app.Models;

public sealed class DependencyStatusInfo
{
    public string Name { get; init; } = string.Empty;

    public bool IsOk { get; init; }

    public bool IsConfigured { get; init; } = true;

    public string StatusText { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
}
