using media_management_app.Models;
using media_management_app.Services.Events;

namespace media_management_app.Services.Symlink;

public interface ISymlinkSyncService
{
    SymlinkSyncResult SyncItem(SourceItem item, string? linkedPath = null, IReadOnlyList<SourceItem>? linkedGroup = null);

    SymlinkSyncResult RemoveItem(SourceItem item, string? linkedPath = null, IReadOnlyList<SourceItem>? linkedGroup = null);

    /// <summary>Moves the Jellyfin symlinks of links that moved to another episode number (audit L3-09).</summary>
    SymlinkSyncResult MoveItems(IReadOnlyList<LibraryLinkMove> moves);

    Dictionary<string, List<SourceItem>> GroupLinkedItemsByPath(IEnumerable<SourceItem> items);

    SymlinkSyncResult ReconcileAll();

    void PruneEmptyFolders(string? startDirectory);
}
