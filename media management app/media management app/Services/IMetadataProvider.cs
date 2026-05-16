using media_management_app.Models;

namespace media_management_app.Services;

public interface IMetadataProvider
{
    Task<LibraryItem?> EnrichAsync(SourceItem item, CancellationToken cancellationToken = default);
}
