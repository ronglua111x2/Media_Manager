using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services.UiSnapshot;

/// <summary>
/// Guards for <c>--ui-snapshots</c>: the run must use a sandbox copy of the state folder, and every
/// external service in that copy is cut off before any screen loads. See docs/UI_SNAPSHOTS.md.
/// </summary>
public static class UiSnapshotSandbox
{
    /// <summary>Unreachable loopback URL (discard port): connections fail at once, nothing is sent anywhere.</summary>
    public const string UnreachableUrl = "http://127.0.0.1:9";

    /// <summary>
    /// Returns why <paramref name="stateFolder"/> must not be used for a snapshot run, or null when it is a sandbox.
    /// Refuses the legacy live folder, the LocalAppData default, and the folder the pointer file names.
    /// </summary>
    public static string? GetStateFolderRefusal(string? stateFolder, string? pointerTarget)
    {
        if (string.IsNullOrWhiteSpace(stateFolder))
        {
            return "UI snapshots need --state-folder pointing at a sandbox copy.";
        }

        var candidate = Normalize(stateFolder);
        var forbidden = new List<string>
        {
            AppConstants.LegacyStateFolder,
            AppConstants.DefaultStateFolder
        };
        if (!string.IsNullOrWhiteSpace(pointerTarget))
        {
            forbidden.Add(pointerTarget);
        }

        foreach (var live in forbidden)
        {
            var liveFull = Normalize(live);
            if (string.Equals(candidate, liveFull, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(liveFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return $"Refusing UI snapshots: '{stateFolder}' is (or is inside) the live state folder '{live}'. Use a sandbox copy.";
            }
        }

        return null;
    }

    /// <summary>Reads the LocalAppData pointer file (read-only). Null when it is missing or unreadable.</summary>
    public static string? TryReadPointerTarget()
    {
        try
        {
            var path = AppConstants.PointerFilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Cuts off qBittorrent, Jellyfin, WARP, Gemini, TMDB, Google Drive backup, Auto-Track, symlink sync
    /// and Windows autostart in the sandbox settings. Screens still show the copied library,
    /// recipes and cached posters. Callers save the result into the sandbox folder so a reload keeps it.
    /// </summary>
    public static void CutOffExternalServices(AppSettings settings, string stateFolder)
    {
        var missingExe = Path.Combine(stateFolder, "ui-snapshot-disabled", "missing.exe");

        settings.AutoTorrent ??= new AutoTorrentSettings();
        settings.AutoTorrent.QbittorrentWebUiUrl = UnreachableUrl;
        settings.AutoTorrent.Password = null;
        settings.AutoTorrent.ApiKey = null;
        settings.AutoTorrent.AutoLinkCompletedDownloads = false;
        settings.AutoTorrent.ProcessRestart ??= new QbittorrentProcessRestartSettings();
        settings.AutoTorrent.ProcessRestart.Enabled = false;
        settings.AutoTorrent.ProcessRestart.ExecutablePath = missingExe;

        settings.Warp ??= new WarpSettings();
        settings.Warp.Enabled = false;
        settings.Warp.ExecutablePath = missingExe;
        settings.Warp.AutoRecoverOnSsl = false;
        settings.Warp.SwitchModeForTmdb = false;
        settings.Warp.ForceTunnelForHunt = false;

        settings.AutoTrack ??= new AutoTrackSettings();
        settings.AutoTrack.Enabled = false;
        settings.AutoTrack.Jellyfin ??= new JellyfinRefreshSettings();
        settings.AutoTrack.Jellyfin.Enabled = false;
        settings.AutoTrack.Jellyfin.BaseUrl = UnreachableUrl;
        settings.AutoTrack.Jellyfin.ApiKey = null;
        settings.AutoTrack.Jellyfin.LogPath = null;
        settings.AutoTrack.Jellyfin.EnableLogEarlyDisconnect = false;

        settings.Symlink ??= new SymlinkSettings();
        settings.Symlink.Enabled = false;
        settings.Symlink.SyncOnStartup = false;
        settings.Symlink.UnifiedRoot = Path.Combine(stateFolder, "ui-snapshot-symlinks");

        settings.TmdbReadAccessToken = null;

        settings.Gemini ??= new GeminiSettings();
        settings.Gemini.Enabled = false;
        settings.Gemini.ApiKey = null;

        settings.Backup ??= new BackupSettings();
        settings.Backup.Enabled = false;
        settings.Backup.CredentialsFilePath = null;

        settings.Startup ??= new AppStartupSettings();
        settings.Startup.RunAtStartup = false;
        settings.Startup.StartMinimized = false;
        settings.Startup.CloseToTray = false;
        settings.Startup.SetupCompleted = true;

        settings.Notifications ??= new NotificationSettings();
        foreach (var kind in settings.Notifications.EnabledByKind.Keys.ToList())
        {
            settings.Notifications.EnabledByKind[kind] = false;
        }
    }

    /// <summary>
    /// Clears saved list filters so Library and Torrent show the whole list, as on a fresh visit.
    /// Sort order, theme and selection are kept.
    /// </summary>
    public static void ResetTransientUiState(AppSettings settings)
    {
        settings.Ui ??= new UiSettings();
        settings.Ui.LibraryMediaSearchQuery = string.Empty;
        settings.Ui.TorrentMediaSearchQuery = string.Empty;
        settings.Ui.LibraryWatchStatusFilters = null;
        settings.Ui.LibraryWatchStatusFilter = null;
        settings.Ui.TorrentWatchStatusFilters = null;
        settings.Ui.TorrentWatchStatusFilter = null;
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
