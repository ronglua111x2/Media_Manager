namespace media_management_app.Models;

public sealed class WindowsNotificationRequest
{
    public required string Title { get; init; }

    public required string Message { get; init; }

    /// <summary>null/empty = auto-generate unique tag so toasts stack.</summary>
    public string? Tag { get; init; }

    public string Group { get; init; } = "MediaManager";

    /// <summary>Large banner image at the bottom of the toast.</summary>
    public string? HeroImagePathOrUrl { get; init; }

    /// <summary>Small square thumbnail beside the title/body (app logo override).</summary>
    public string? AppLogoOverridePathOrUrl { get; init; }
}
