using media_management_app.Models;

namespace media_management_app.Services;

public interface ILibraryPathResolver
{
    bool TryResolveMediaRoot(SourceItem item, out string mediaRoot, out string? errorMessage);

    bool TryResolveGeneratedLibraryRoot(SourceItem item, out string libraryRoot, out string? errorMessage);

    IReadOnlyList<string> GetPreviewRoots(IEnumerable<string> sourceFolders);
}
