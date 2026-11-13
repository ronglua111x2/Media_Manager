using media_management_app.Models;

namespace media_management_app.Services;

public interface IHardlinkService
{
    string BuildOutputPath(SourceItem item, string mediaRoot);

    bool CreateHardLink(SourceItem item, out string? createdPath, out string? errorMessage);

    bool RemoveHardLink(SourceItem item, out string? removedPath, out string? errorMessage);

    /// <summary>
    /// Whether <see cref="RemoveHardLink"/> would succeed for this item right now, without changing anything:
    /// the drive is reachable, the link is inside the library folder and no program holds it open.
    /// </summary>
    bool CanRemoveHardLink(SourceItem item, out string? reason);

    /// <summary>A storage probe that knows the currently configured library folder of each drive.</summary>
    LinkStorageProbe CreateLinkStorageProbe();
}
