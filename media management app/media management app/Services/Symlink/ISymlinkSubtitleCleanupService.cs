namespace media_management_app.Services.Symlink;

/// <summary>
/// Service for cleaning up subtitle and companion files associated with symlinks in Jellyfin library.
/// Handles both TV episodes and movies.
/// </summary>
public interface ISymlinkSubtitleCleanupService
{
    /// <summary>
    /// Deletes all subtitle files (.srt, .ass, .ssa, .sub, .vtt, etc.) associated with a symlink.
    /// This method is called before removing the symlink to ensure companion files are cleaned up.
    /// Supports both episodes (in Season folders) and movies (in movie folders).
    /// </summary>
    /// <param name="symlinkPath">Full path to the symlink file (e.g., "C:\JellyfinLibrary\Shows\Show Title\Season 01\Episode.mkv")</param>
    void DeleteSubtitleFiles(string symlinkPath);
}
