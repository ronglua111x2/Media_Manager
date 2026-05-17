using media_management_app.Common;

namespace media_management_app.Models;

public sealed class AppSettings
{
    public string StateFolder { get; set; } = AppConstants.DefaultStateFolder;

    public List<string> SourceFolders { get; set; } = [];

    public LibraryRootMode LibraryRootMode { get; set; } = LibraryRootMode.AutoPerDrive;

    public string DefaultLibraryFolderName { get; set; } = AppConstants.DefaultLibraryFolderName;

    public Dictionary<string, string> DriveLibraryRoots { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? OutputLibraryFolder { get; set; }

    public string? TmdbReadAccessToken { get; set; }
}
