using media_management_app.Search;

namespace SearchBench;

/// <summary>
/// The bench's request governor shares the app's indexer budget (audit 07d GOV-07): it loads
/// &lt;state&gt;/indexer-state.json (today's requests, open breakers, cooldowns) and writes its own requests back when it
/// ends, under the state folder's lock. While the app holds that lock (it searches natively), the bench refuses to
/// run: two processes on one IP would each spend the full budget. <c>--force 1</c> runs anyway with a private
/// governor that is not saved.
/// </summary>
internal sealed class BenchState : IDisposable
{
    private readonly string _path;
    private readonly IDisposable? _lock;
    private readonly Dictionary<string, IndexerStateSnapshot> _baseline;

    private BenchState(string path, IDisposable? stateLock, IReadOnlyList<IndexerStateSnapshot> loaded, bool cautious)
    {
        _path = path;
        _lock = stateLock;
        _baseline = loaded.ToDictionary(state => state.IndexerId, state => state.Clone(), StringComparer.OrdinalIgnoreCase);
        Governor = new IndexerRequestGovernor(restored: loaded);
        if (cautious)
        {
            Governor.StartCautiously(NativeIndexerCatalog.AllIds);
        }
    }

    public IndexerRequestGovernor Governor { get; }

    public static BenchState Open(Dictionary<string, string> args)
    {
        var folder = args.GetValueOrDefault("state") ?? @"D:\MediaManagerState";
        var path = Path.Combine(folder, IndexerStateFile.FileName);
        var stateLock = IndexerStateFile.TryLock(folder);
        if (stateLock is null)
        {
            if (args.GetValueOrDefault("force") is not ("1" or "true"))
            {
                throw new InvalidOperationException(
                    $"{IndexerStateFile.LockFileName} in {folder} is held: the app (or another bench) is searching natively. " +
                    "Close it first, or pass --force 1 to run with a private budget (the sites then see both).");
            }

            Console.Error.WriteLine("  --force: the indexer state is locked by another process; using a private governor that is not saved.");
            return new BenchState(path, null, [], cautious: false);
        }

        var loaded = IndexerStateFile.Load(path);
        if (loaded.Warning is not null)
        {
            Console.Error.WriteLine($"  {loaded.Warning}");
        }

        Console.Error.WriteLine($"  indexer state: {loaded.States.Count} site(s) loaded from {path}; usage is written back at the end.");
        return new BenchState(path, stateLock, loaded.States, loaded.Cautious);
    }

    /// <summary>Adds the bench's requests (and any breaker it opened) to the current file, then releases the lock.</summary>
    public void Dispose()
    {
        if (_lock is null)
        {
            return;
        }

        try
        {
            // Re-read: an app build without the lock may have written meanwhile; add our share instead of overwriting.
            var current = IndexerStateFile.Load(_path).States.ToDictionary(state => state.IndexerId, state => state.Clone(), StringComparer.OrdinalIgnoreCase);
            foreach (var mine in Governor.Snapshot())
            {
                var before = _baseline.GetValueOrDefault(mine.IndexerId);
                var spent = before is not null && before.DayKey == mine.DayKey ? mine.RequestsToday - before.RequestsToday : mine.RequestsToday;
                if (!current.TryGetValue(mine.IndexerId, out var merged))
                {
                    current[mine.IndexerId] = mine.Clone();
                    continue;
                }

                if (merged.DayKey == mine.DayKey)
                {
                    merged.RequestsToday += Math.Max(0, spent);
                }
                else if (string.CompareOrdinal(mine.DayKey, merged.DayKey) > 0)
                {
                    merged.DayKey = mine.DayKey;
                    merged.RequestsToday = mine.RequestsToday;
                }

                if (mine.LastRequestUtc > merged.LastRequestUtc || merged.LastRequestUtc is null)
                {
                    merged.LastRequestUtc = mine.LastRequestUtc;
                }

                // A block the bench ran into must survive for the app.
                if (mine.Breaker != IndexerBreakerState.Closed && (mine.OpenUntilUtc ?? DateTimeOffset.MinValue) > (merged.OpenUntilUtc ?? DateTimeOffset.MinValue))
                {
                    merged.Breaker = mine.Breaker;
                    merged.OpenUntilUtc = mine.OpenUntilUtc;
                    merged.OpenCount = Math.Max(merged.OpenCount, mine.OpenCount);
                    merged.LastError = mine.LastError;
                    merged.LastErrorUtc = mine.LastErrorUtc;
                }

                if ((mine.CooldownUntilUtc ?? DateTimeOffset.MinValue) > (merged.CooldownUntilUtc ?? DateTimeOffset.MinValue))
                {
                    merged.CooldownUntilUtc = mine.CooldownUntilUtc;
                    merged.RateLimitLevel = Math.Max(merged.RateLimitLevel, mine.RateLimitLevel);
                }
            }

            IndexerStateFile.Save(_path, current.Values.OrderBy(state => state.IndexerId, StringComparer.OrdinalIgnoreCase).ToList());
            Console.Error.WriteLine($"  indexer state written back to {_path}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"  could not write {_path}: {ex.Message}");
        }
        finally
        {
            _lock.Dispose();
        }
    }
}
