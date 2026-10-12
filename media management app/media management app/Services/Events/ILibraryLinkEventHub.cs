using media_management_app.Models;

namespace media_management_app.Services.Events;

public interface ILibraryLinkEventHub
{
    event EventHandler<LibraryLinkEventArgs>? HardlinkCreated;

    event EventHandler<LibraryLinkEventArgs>? HardlinkRemoved;

    event EventHandler<LibraryLinkBatchEventArgs>? HardlinksCreated;

    event EventHandler<LibraryLinkBatchEventArgs>? HardlinksRemoved;

    event EventHandler? SymlinkStateChanged;

    IDisposable BeginBulkMutation();

    void PublishHardlinkCreated(SourceItem item, string linkedPath);

    void PublishHardlinkRemoved(SourceItem item, string linkedPath);

    void PublishSymlinkStateChanged();
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
