namespace media_management_app.Models;

public sealed class WindowsNotificationRequest
{
    public required string Title { get; init; }

    public required string Message { get; init; }

    /// <summary>null/empty = auto-generate unique tag so toasts stack.</summary>
    public string? Tag { get; init; }

    public string Group { get; init; } = "MediaManager";

    /// <summary>Local file path or https URL for large cover art (hero image).</summary>
    public string? HeroImagePathOrUrl { get; init; }

    /// <summary>Optional small logo/thumbnail override (path or https URL).</summary>
    public string? AppLogoOverridePathOrUrl { get; init; }
}
