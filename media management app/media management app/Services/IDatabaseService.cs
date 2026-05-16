using media_management_app.Models;

namespace media_management_app.Services;

public interface IDatabaseService
{
    void Initialize(string stateFolder);

    IReadOnlyList<SourceItem> GetSourceItems();

    void UpsertSourceItem(SourceItem item);

    void UpsertSourceItems(IEnumerable<SourceItem> items);

    void UpdateSourceItem(SourceItem item);

    int MarkMissingSourceItems(IEnumerable<string> sourceFolders, IEnumerable<string> seenFilePaths);

    int DeleteSourceItemsByState(ItemState state);
}
