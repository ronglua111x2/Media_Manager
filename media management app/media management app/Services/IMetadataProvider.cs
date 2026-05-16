using media_management_app.Models;

namespace media_management_app.Services;

public interface IMetadataProvider
{
    Task<MetadataMatchResult> MatchTvSeriesAsync(SourceItem item, CancellationToken cancellationToken = default);
}
