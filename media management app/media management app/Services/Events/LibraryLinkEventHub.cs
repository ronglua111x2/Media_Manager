using media_management_app.Models;

namespace media_management_app.Services.Events;

public sealed class LibraryLinkEventHub : ILibraryLinkEventHub
{
    public event EventHandler<LibraryLinkEventArgs>? HardlinkCreated;

    public event EventHandler<LibraryLinkEventArgs>? HardlinkRemoved;

    public void PublishHardlinkCreated(SourceItem item, string linkedPath)
    {
        HardlinkCreated?.Invoke(this, new LibraryLinkEventArgs(item, linkedPath));
    }

    public void PublishHardlinkRemoved(SourceItem item, string linkedPath)
    {
        HardlinkRemoved?.Invoke(this, new LibraryLinkEventArgs(item, linkedPath));
    }
}
