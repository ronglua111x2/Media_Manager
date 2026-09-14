using media_management_app.Models;

namespace media_management_app.Common;

/// <summary>
/// Default probe candidates and version labels for first-run host scan.
/// Paths are starting points only — Browse overrides them in session (Phase 2).
/// See docs/first-run/02-phase-host-scanner.md.
/// </summary>
public static class HostSoftwareCatalog
{
    public static readonly Version MinWindowsVersion = new(10, 0, 17763);

    public const string WindowsRequiredLabel = "Windows 10 1809+ (10.0.17763)";

    public const string AppRequiredLabel = "Self-contained win-x64";

    public const string WebView2RequiredLabel = "Evergreen WebView2 Runtime";

    public static readonly Version MinQbittorrentVersion = new(5, 1);

    public const string QbittorrentRequiredLabel = "qBittorrent 5.1 or 5.2+";

    public const string QbittorrentProcessName = "qbittorrent";

    public const string QbittorrentDefaultWebUiUrl = "http://127.0.0.1:8080";

    public const string WarpRequiredLabel = "warp-cli (any version that runs status)";

    public const string WarpDefaultWebUiHint = "Cloudflare WARP desktop";

    public static readonly string[] WarpProcessNames = ["Cloudflare WARP", "warp-svc"];

    public const string JellyfinRequiredLabel = "No local min version (remote server is OK)";

    public const string JellyfinDefaultBaseUrl = "http://127.0.0.1:8096";

    public static readonly string[] JellyfinProcessNames = ["jellyfin", "Jellyfin.Server"];

    public const int JellyfinDefaultPort = 8096;

    public const string TmdbRequiredLabel = "TMDB API v3 bearer token";

    public const string GeminiRequiredLabel = "Gemini API key (optional)";

    public const string DriveRequiredLabel = "Google Drive OAuth desktop client (optional)";

    public const string NotConfiguredNote = "Not configured in this folder.";
}
