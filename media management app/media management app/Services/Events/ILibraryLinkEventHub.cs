using media_management_app.Models;

namespace media_management_app.Services.Events;

public interface ILibraryLinkEventHub
{
    event EventHandler<LibraryLinkEventArgs>? HardlinkCreated;

    event EventHandler<LibraryLinkEventArgs>? HardlinkRemoved;

    void PublishHardlinkCreated(SourceItem item, string linkedPath);

    void PublishHardlinkRemoved(SourceItem item, string linkedPath);
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
