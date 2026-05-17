using media_management_app.Models;

namespace media_management_app.Services;

public interface ISourceReconciliationService
{
    SourceReconciliationResult ReconcileMissingSourceItems(IEnumerable<string> sourceFolders, IEnumerable<string> seenFilePaths);
}
