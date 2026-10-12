using media_management_app.Models;

namespace media_management_app.Services;

public interface IJellyfinLibraryRefreshService : IDisposable
{
    /// <summary>
    /// Enqueues a symlink path for debounced Jellyfin notify when the item is
    /// Auto-Track eligible (auto-tracked, default order).
    /// </summary>
    void EnqueueFromSourceItem(SourceItem item, TrackedShow? show = null);

    /// <summary>
    /// Enqueues already-filtered symlink paths (e.g. SyncNow touched paths).
    /// </summary>
    void EnqueuePaths(IEnumerable<string> symlinkPaths);

    /// <summary>
    /// Immediately flushes the debounce queue (probe TMDB / WARP / notify).
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);

    Task<string> TestConnectionAsync(CancellationToken cancellationToken = default);
}
