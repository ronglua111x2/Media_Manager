using media_management_app.Models;

namespace media_management_app.Services.Events;

public sealed class LibraryLinkEventHub : ILibraryLinkEventHub
{
    private readonly object _gate = new();
    private readonly NestedBulkEventCollector<LibraryLinkEventArgs> _createdBuffer = new();
    private readonly NestedBulkEventCollector<LibraryLinkEventArgs> _removedBuffer = new();
    private IDisposable? _createdScope;
    private IDisposable? _removedScope;
    private int _depth;

    public LibraryLinkEventHub()
    {
        _createdBuffer.Completed += (_, items) =>
            HardlinksCreated?.Invoke(this, new LibraryLinkBatchEventArgs(items));
        _removedBuffer.Completed += (_, items) =>
            HardlinksRemoved?.Invoke(this, new LibraryLinkBatchEventArgs(items));
    }

    public event EventHandler<LibraryLinkEventArgs>? HardlinkCreated;

    public event EventHandler<LibraryLinkEventArgs>? HardlinkRemoved;

    public event EventHandler<LibraryLinkBatchEventArgs>? HardlinksCreated;

    public event EventHandler<LibraryLinkBatchEventArgs>? HardlinksRemoved;

    public event EventHandler? SymlinkStateChanged;

    public IDisposable BeginBulkMutation()
    {
        lock (_gate)
        {
            _depth++;
            if (_depth == 1)
            {
                _createdScope = _createdBuffer.BeginScope();
                _removedScope = _removedBuffer.BeginScope();
            }

            return new BulkMutationScope(this);
        }
    }

    public void PublishHardlinkCreated(SourceItem item, string linkedPath)
    {
        var args = new LibraryLinkEventArgs(item, linkedPath);
        if (_createdBuffer.TryQueue(args))
        {
            return;
        }

        HardlinkCreated?.Invoke(this, args);
    }

    public void PublishHardlinkRemoved(SourceItem item, string linkedPath)
    {
        var args = new LibraryLinkEventArgs(item, linkedPath);
        if (_removedBuffer.TryQueue(args))
        {
            return;
        }

        HardlinkRemoved?.Invoke(this, args);
    }

    public void PublishSymlinkStateChanged()
    {
        SymlinkStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EndBulkMutation()
    {
        IDisposable? createdScope;
        IDisposable? removedScope;
        lock (_gate)
        {
            if (_depth <= 0)
            {
                return;
            }

            _depth--;
            if (_depth > 0)
            {
                return;
            }

            createdScope = _createdScope;
            removedScope = _removedScope;
            _createdScope = null;
            _removedScope = null;
        }

        createdScope?.Dispose();
        removedScope?.Dispose();
    }

    private sealed class BulkMutationScope : IDisposable
    {
        private LibraryLinkEventHub? _owner;

        public BulkMutationScope(LibraryLinkEventHub owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.EndBulkMutation();
        }
    }
}
