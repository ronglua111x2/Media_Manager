using media_management_app.Common;

namespace media_management_app.Models;

public sealed class AppSettings
{
    public string StateFolder { get; set; } = AppConstants.DefaultStateFolder;

    public AutoTorrentSettings AutoTorrent { get; set; } = new();

    public WarpSettings Warp { get; set; } = new();

    public AutoTrackSettings AutoTrack { get; set; } = new();

    public LogSettings Logs { get; set; } = new();

    public AppStartupSettings Startup { get; set; } = new();

    public UiSettings Ui { get; set; } = new();

    public NotificationSettings Notifications { get; set; } = new();

    public List<string> SourceFolders { get; set; } = [];

    public LibraryRootMode LibraryRootMode { get; set; } = LibraryRootMode.AutoPerDrive;

    public string DefaultLibraryFolderName { get; set; } = AppConstants.DefaultLibraryFolderName;

    public Dictionary<string, string> DriveLibraryRoots { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public SymlinkSettings Symlink { get; set; } = new();

    public string? OutputLibraryFolder { get; set; }

    public string? TmdbReadAccessToken { get; set; }

    public GeminiSettings Gemini { get; set; } = new();

    public BackupSettings Backup { get; set; } = new();
}
