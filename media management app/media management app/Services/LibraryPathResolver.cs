using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class LibraryPathResolver : ILibraryPathResolver
{
    private readonly ISettingsService _settingsService;

    public LibraryPathResolver(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool TryResolveMediaRoot(SourceItem item, out string mediaRoot, out string? errorMessage)
    {
        mediaRoot = string.Empty;
        if (!TryResolveGeneratedLibraryRoot(item, out var libraryRoot, out errorMessage))
        {
            return false;
        }

        var mediaFolder = item.MediaKind switch
        {
            MediaKind.TvEpisode => AppConstants.ShowsFolderName,
            MediaKind.Movie => AppConstants.MoviesFolderName,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(mediaFolder))
        {
            errorMessage = $"Unsupported media kind for hardlink output: {item.MediaKind}";
            return false;
        }

        mediaRoot = Path.Combine(libraryRoot, mediaFolder);
        return true;
    }

    public bool TryResolveGeneratedLibraryRoot(SourceItem item, out string libraryRoot, out string? errorMessage)
    {
        libraryRoot = string.Empty;
        errorMessage = null;

        var sourceRoot = Path.GetPathRoot(item.FilePath);
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            errorMessage = $"Could not determine source drive for {item.FilePath}";
            return false;
        }

        libraryRoot = ResolveLibraryRootForDrive(sourceRoot);
        return true;
    }

    public IReadOnlyList<string> GetPreviewRoots(IEnumerable<string> sourceFolders)
    {
        return sourceFolders
            .Select(Path.GetPathRoot)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .Select(ResolveLibraryRootForDrive)
            .ToList();
    }

    private string ResolveLibraryRootForDrive(string driveRoot)
    {
        var normalizedDriveRoot = Path.GetPathRoot(driveRoot) ?? driveRoot;
        if (_settingsService.Current.DriveLibraryRoots.TryGetValue(normalizedDriveRoot, out var overrideRoot) &&
            !string.IsNullOrWhiteSpace(overrideRoot))
        {
            return overrideRoot;
        }

        return Path.Combine(normalizedDriveRoot, _settingsService.Current.DefaultLibraryFolderName);
    }
}
