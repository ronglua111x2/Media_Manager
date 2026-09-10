using media_management_app.Models;

namespace media_management_app.Services;

public interface IJellyfinMediaNavigationService
{
    Task<JellyfinMediaNavigationResult> OpenEpisodeAsync(
        JellyfinEpisodeTarget target,
        CancellationToken cancellationToken = default);
}
