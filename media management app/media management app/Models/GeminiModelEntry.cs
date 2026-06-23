namespace media_management_app.Models;

public sealed class GeminiModelEntry
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Deprecated { get; set; }

    public string? Replacement { get; set; }

    public string? Notes { get; set; }

    public bool SupportsTextMapping { get; set; } = true;

    public string GetDisplayLabel() =>
        Deprecated ? $"{DisplayName} (deprecated)" : DisplayName;
}
