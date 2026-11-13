using media_management_app.Models;

namespace media_management_app.Services.Events;

public interface ILibraryLinkEventHub
{
    event EventHandler<LibraryLinkEventArgs>? HardlinkCreated;

    event EventHandler<LibraryLinkEventArgs>? HardlinkRemoved;

    event EventHandler<LibraryLinkBatchEventArgs>? HardlinksCreated;

    event EventHandler<LibraryLinkBatchEventArgs>? HardlinksRemoved;

    /// <summary>Library links that now belong to another episode number (TMDB renumbered, audit L3-09).</summary>
    event EventHandler<LibraryLinkMoveBatchEventArgs>? HardlinksMoved;

    event EventHandler? SymlinkStateChanged;

    IDisposable BeginBulkMutation();

    void PublishHardlinkCreated(SourceItem item, string linkedPath);

    void PublishHardlinkRemoved(SourceItem item, string linkedPath);

    /// <summary>Raised at once, never buffered: the moves are one batch already.</summary>
    void PublishHardlinksMoved(IReadOnlyList<LibraryLinkMove> moves);

    void PublishSymlinkStateChanged();
}

/// <summary>A record whose library link moved to another episode number: <see cref="Item"/> as saved now, <see cref="Previous"/> as it was.</summary>
public sealed class LibraryLinkMove
{
    public LibraryLinkMove(SourceItem item, SourceItem previous)
    {
        Item = item;
        Previous = previous;
    }

    public SourceItem Item { get; }

    public SourceItem Previous { get; }
}

public sealed class LibraryLinkMoveBatchEventArgs : EventArgs
{
    public LibraryLinkMoveBatchEventArgs(IReadOnlyList<LibraryLinkMove> moves)
    {
        Moves = moves;
    }

    public IReadOnlyList<LibraryLinkMove> Moves { get; }
}

public sealed class LibraryLinkEventArgs : EventArgs
{
    public LibraryLinkEventArgs(SourceItem item, string linkedPath)
    {
        Item = item;
        LinkedPath = linkedPath;
    }

    public SourceItem Item { get; }

    public string LinkedPath { get; }
}

public sealed class LibraryLinkBatchEventArgs : EventArgs
{
    public LibraryLinkBatchEventArgs(IReadOnlyList<LibraryLinkEventArgs> items)
    {
        Items = items;
    }

    public IReadOnlyList<LibraryLinkEventArgs> Items { get; }
}
