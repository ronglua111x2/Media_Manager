using media_management_app.Models;

namespace media_management_app.Services.Symlink;

public interface ISymlinkSyncService
{
    SymlinkSyncResult SyncItem(SourceItem item, string? linkedPath = null, IReadOnlyList<SourceItem>? linkedGroup = null);

    SymlinkSyncResult RemoveItem(SourceItem item, string? linkedPath = null, IReadOnlyList<SourceItem>? linkedGroup = null);

    Dictionary<string, List<SourceItem>> GroupLinkedItemsByPath(IEnumerable<SourceItem> items);

    SymlinkSyncResult ReconcileAll();

    void PruneEmptyFolders(string? startDirectory);
}
