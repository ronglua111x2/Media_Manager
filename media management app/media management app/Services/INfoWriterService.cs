using media_management_app.Models;

namespace media_management_app.Services;

public interface INfoWriterService
{
    void WriteEpisodeNfoIfNeeded(string symlinkPath, TrackedEpisode episode, TrackedShow show);

    void WriteTvShowNfo(string showFolderPath, TrackedShow show);

    void DeleteEpisodeNfo(string symlinkPath);

    void DeleteTvShowNfo(string showFolderPath);

    bool HasRemainingEpisodeArtifacts(string showFolderPath);

    void CleanupOrphanEpisodeNfos(string showFolderPath, IEnumerable<string> activeSymlinkPaths);
}
