using media_management_app.Models;

namespace media_management_app.Services;

public interface IScannerService
{
    IReadOnlyList<SourceItem> Scan(IEnumerable<string> sourceFolders);
}
