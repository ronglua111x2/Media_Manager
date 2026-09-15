namespace media_management_app.Services;

/// <summary>
/// Nested-scope event collector. Items queue while a scope is active and drain only
/// when the outermost scope completes. Inner dispose and exception-safe dispose stay safe.
/// </summary>
public sealed class NestedBulkEventCollector<T>
{
    private readonly object _gate = new();
    private int _depth;
    private List<T>? _queued;

    public event EventHandler<IReadOnlyList<T>>? Completed;

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _depth > 0;
            }
        }
    }

    public IDisposable BeginScope()
    {
        lock (_gate)
        {
            _depth++;
            _queued ??= [];
            return new Scope(this);
        }
    }

    public bool TryQueue(T item)
    {
        lock (_gate)
        {
            if (_depth <= 0)
            {
                return false;
            }

            _queued ??= [];
            _queued.Add(item);
            return true;
        }
    }

    private void OnScopeDisposed()
    {
        IReadOnlyList<T>? drained = null;
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

            drained = _queued ?? [];
            _queued = null;
        }

        if (drained is { Count: > 0 })
        {
            Completed?.Invoke(this, drained);
        }
    }

    private sealed class Scope : IDisposable
    {
        private NestedBulkEventCollector<T>? _owner;

        public Scope(NestedBulkEventCollector<T> owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.OnScopeDisposed();
        }
    }
}
