using media_management_app.Models;

namespace media_management_app.Services;

public interface IHardlinkService
{
    string BuildOutputPath(SourceItem item, string outputRoot);

    bool CreateHardLink(SourceItem item, string outputRoot, out string? createdPath, out string? errorMessage);

    bool RemoveHardLink(SourceItem item, out string? removedPath, out string? errorMessage);
}
