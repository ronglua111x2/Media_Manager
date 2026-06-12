using media_management_app.Models;

namespace media_management_app.Services;

public interface IMetadataProvider
{
    Task<MetadataMatchResult> MatchTvSeriesAsync(SourceItem item, CancellationToken cancellationToken = default);

    Task<MetadataMatchResult> MatchTvSeriesAsync(TvSeriesMatchRequest request, CancellationToken cancellationToken = default);

    Task<MetadataValidationResult> ValidateTvSeriesMatchAsync(TvSeriesMatchRequest request, string providerId, CancellationToken cancellationToken = default);

    Task<MovieMetadataMatchResult> MatchMovieAsync(SourceItem item, CancellationToken cancellationToken = default);

    Task<EpisodeMappingResult> MapTvEpisodeAsync(SourceItem item, CancellationToken cancellationToken = default);
}
