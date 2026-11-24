using media_management_app.Common;

namespace media_management_app.Services;

/// <summary>
/// Starts the Cart's own search for specific orders without opening the Cart tab
/// (Find "Add + search"). Implemented by the Cart workspace view model.
/// </summary>
public interface ICartSearchLauncher
{
    /// <summary>
    /// Queues a Cart search for <paramref name="orderIds"/> of one media item. It runs through the
    /// same path as Run Cart, after any cart operation already running. Adding torrents stays the
    /// user-approved Add step with all intake checks.
    /// </summary>
    void QueueSearch(MediaKind kind, long mediaId, string title, IReadOnlyCollection<long> orderIds);

    /// <summary>Opens the Cart tab with this media item selected.</summary>
    void OpenCart(MediaKind kind, long mediaId);

    /// <summary>Raised on the UI thread when a queued search finishes (status text for that item).</summary>
    event EventHandler<CartSearchFinishedEventArgs>? SearchFinished;
}

public sealed class CartSearchFinishedEventArgs(MediaKind kind, long mediaId, string title, string status) : EventArgs
{
    public MediaKind Kind { get; } = kind;

    public long MediaId { get; } = mediaId;

    public string Title { get; } = title;

    public string Status { get; } = status;
}
