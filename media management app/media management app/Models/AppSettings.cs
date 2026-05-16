using media_management_app.Common;

namespace media_management_app.Models;

public sealed class AppSettings
{
    public string StateFolder { get; set; } = AppConstants.DefaultStateFolder;

    public List<string> SourceFolders { get; set; } = [];

    public string OutputLibraryFolder { get; set; } = Path.Combine(AppConstants.DefaultStateFolder, AppConstants.DefaultLibraryFolderName);

    public string? TmdbReadAccessToken { get; set; }
}
