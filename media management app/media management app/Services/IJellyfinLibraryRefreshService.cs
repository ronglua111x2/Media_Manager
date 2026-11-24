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

    /// <summary>True when Jellyfin refresh is enabled with a base URL and API key.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends the given symlink paths to Jellyfin now (no debounce wait), merged with any
    /// pending debounce batch so nothing is sent twice. Paths missing on disk are skipped.
    /// Empty input or "not configured" makes no HTTP call and does not throw. Returns once
    /// Jellyfin accepted (or rejected) the notify; any post-notify WARP hold keeps running
    /// in the background.
    /// </summary>
    Task<JellyfinRefreshNowResult> RefreshNowAsync(
        IEnumerable<string> symlinkPaths,
        CancellationToken cancellationToken = default);

    Task<string> TestConnectionAsync(CancellationToken cancellationToken = default);
}
