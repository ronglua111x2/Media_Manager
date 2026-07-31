namespace media_management_app.Common;

/// <summary>
/// Default toast heroes. Drop PNGs into Assets/notifications/ (see README.txt there):
/// warp-hero.png, jellyfin-hero.png. Missing files simply omit the hero image.
/// </summary>
public static class NotificationHeroImages
{
    private static readonly Dictionary<NotificationKind, string> RelativePaths = new()
    {
        [NotificationKind.WarpRecovered] = Path.Combine("Assets", "notifications", "warp-hero.png"),
        [NotificationKind.WarpDisconnected] = Path.Combine("Assets", "notifications", "warp-hero.png"),
        [NotificationKind.JellyfinPathNotified] = Path.Combine("Assets", "notifications", "jellyfin-hero.png"),
        [NotificationKind.JellyfinRefreshWindowEnded] = Path.Combine("Assets", "notifications", "jellyfin-hero.png")
    };

    public static bool TryGetDefault(NotificationKind kind, out string absolutePath)
    {
        absolutePath = string.Empty;
        if (!RelativePaths.TryGetValue(kind, out var relative))
        {
            return false;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, relative);
        if (!File.Exists(candidate))
        {
            return false;
        }

        absolutePath = Path.GetFullPath(candidate);
        return true;
    }
}
