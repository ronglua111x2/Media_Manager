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

    /// <summary>The library path <see cref="CreateHardLink"/> gives this item now; false when no library folder resolves.</summary>
    bool TryBuildLibraryPath(SourceItem item, out string libraryPath, out string? errorMessage);

    /// <summary>
    /// Renames library links all together: every file first gets a temporary name, then its new one, so chains and
    /// swaps work (S01E04 -> S01E05 while S01E05 -> S01E06). On any failure every file is put back and nothing changes.
    /// A rename keeps the file, so it also works when the library link is the only copy (audit L3-09).
    /// </summary>
    bool MoveHardLinks(IReadOnlyList<HardLinkRename> renames, out string? errorMessage);
}

/// <summary>One library link to rename: <see cref="FromPath"/> is its current path, <see cref="Item"/> its record.</summary>
public sealed record HardLinkRename(SourceItem Item, string FromPath, string ToPath);
