namespace media_management_app.Common;

/// <summary>
/// Default small inline toast icons (app logo override beside title/body).
/// Drop square PNGs into Assets/notifications/ (see README.txt):
/// warp-logo.png, jellyfin-logo.png. Missing files simply omit the icon.
/// </summary>
public static class NotificationBrandImages
{
    private static readonly Dictionary<NotificationKind, string> RelativePaths = new()
    {
        [NotificationKind.WarpRecovered] = Path.Combine("Assets", "notifications", "warp-logo.png"),
        [NotificationKind.WarpDisconnected] = Path.Combine("Assets", "notifications", "warp-logo.png"),
        [NotificationKind.JellyfinPathNotified] = Path.Combine("Assets", "notifications", "jellyfin-logo.png"),
        [NotificationKind.JellyfinRefreshWindowEnded] = Path.Combine("Assets", "notifications", "jellyfin-logo.png")
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
