namespace media_management_app.Search;

/// <summary>
/// One search target across all its queries: an episode, a season pack, a movie or a show snapshot (audit 07d
/// GOV-03 B). It carries
/// <list type="bullet">
/// <item>the network requests each site received for this target, for the per-search request cap
/// (<see cref="IndexerPolicy.MaxRequestsPerSearch"/>); cache hits are counted apart and never limited;</item>
/// <item>the backend chosen for the run, so a Settings change in the middle of a run cannot switch half of its
/// queries to another backend (doc 06 §1);</item>
/// <item>the outcome of every native search, so callers can tell "no candidates" from "no search source could be
/// asked" (every site paused, open breaker, budget used).</item>
/// </list>
/// Thread-safe: the queries of one target may run in parallel.
/// </summary>
public sealed class SearchSession
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _requests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _cacheHits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _backends = new(StringComparer.Ordinal);
    private readonly List<IReadOnlyList<IndexerRunTelemetry>> _searches = [];

    /// <summary>Network requests sent to <paramref name="indexerId"/> in this session.</summary>
    public int RequestsTo(string indexerId)
    {
        lock (_gate)
        {
            return _requests.GetValueOrDefault(indexerId);
        }
    }

    /// <summary>Counts one network request unless <paramref name="cap"/> requests were already sent.</summary>
    public bool TryReserveRequest(string indexerId, int cap)
    {
        lock (_gate)
        {
            var sent = _requests.GetValueOrDefault(indexerId);
            if (sent >= cap)
            {
                return false;
            }

            _requests[indexerId] = sent + 1;
            return true;
        }
    }

    /// <summary>Gives back a reserved request that was never sent (the governor refused it).</summary>
    public void ReleaseRequest(string indexerId)
    {
        lock (_gate)
        {
            if (_requests.TryGetValue(indexerId, out var sent) && sent > 0)
            {
                _requests[indexerId] = sent - 1;
            }
        }
    }

    public void CountCacheHit(string indexerId)
    {
        lock (_gate)
        {
            _cacheHits[indexerId] = _cacheHits.GetValueOrDefault(indexerId) + 1;
        }
    }

    public int CacheHitsFor(string indexerId)
    {
        lock (_gate)
        {
            return _cacheHits.GetValueOrDefault(indexerId);
        }
    }

    /// <summary>The backend of <paramref name="key"/> (media kind + recipe) for the whole session: the first answer wins.</summary>
    public string PinBackend(string key, Func<string> resolve)
    {
        lock (_gate)
        {
            if (!_backends.TryGetValue(key, out var backend))
            {
                backend = resolve();
                _backends[key] = backend;
            }

            return backend;
        }
    }

    private string? _backendNote;

    /// <summary>
    /// Why this session searched with qBittorrent although its recipe chose Native/Both (native search off or paused),
    /// for the order status; null when the recipe's backend ran.
    /// </summary>
    public string? BackendNote
    {
        get
        {
            lock (_gate)
            {
                return _backendNote;
            }
        }
    }

    /// <summary>Keeps the first fallback note of the session.</summary>
    public void NoteBackendFallback(string note)
    {
        lock (_gate)
        {
            _backendNote ??= note;
        }
    }

    /// <summary>Remembers the per-site outcome of one native search of this session.</summary>
    public void Record(IReadOnlyList<IndexerRunTelemetry> indexers)
    {
        lock (_gate)
        {
            _searches.Add(indexers);
        }
    }

    public int NativeSearches
    {
        get
        {
            lock (_gate)
            {
                return _searches.Count;
            }
        }
    }

    /// <summary>
    /// True when the session ran native searches and not one of them reached a site: every site was skipped
    /// (paused, disabled, breaker open, budget used), blocked or failed. The search did not run, so "no candidates"
    /// would be wrong.
    /// </summary>
    public bool NoSourceAvailable
    {
        get
        {
            lock (_gate)
            {
                return _searches.Count > 0 && _searches.All(search => !search.Any(SourceAnswered));
            }
        }
    }

    /// <summary>Why no source answered (from the last search), for order status and logs; null when one did.</summary>
    public string? NoSourceReason
    {
        get
        {
            IReadOnlyList<IndexerRunTelemetry> last;
            lock (_gate)
            {
                if (_searches.Count == 0 || _searches.Any(search => search.Any(SourceAnswered)))
                {
                    return null;
                }

                last = _searches[^1];
            }

            return DescribeNoSource(last);
        }
    }

    /// <summary>A site that answered (possibly cut short by the per-search cap).</summary>
    public static bool SourceAnswered(IndexerRunTelemetry run) => run.Status is "ok" or "trimmed";

    /// <summary>"No search source available: …" text for one search whose sites all failed or were skipped.</summary>
    public static string DescribeNoSource(IReadOnlyList<IndexerRunTelemetry> runs)
    {
        if (runs.Count == 0 || runs.All(run => run.Status == "skipped" && run.Detail == "disabled"))
        {
            return "No search source available: no native search site is enabled for this kind of search.";
        }

        if (runs.Any(run => run.Detail?.Contains("kill switch", StringComparison.OrdinalIgnoreCase) == true))
        {
            return "No search source available: native search is paused (Settings > Search engines > Pause all).";
        }

        var reasons = runs
            .Where(run => !(run.Status == "skipped" && run.Detail == "disabled"))
            .Select(run => $"{run.IndexerId} {run.Status}{(string.IsNullOrWhiteSpace(run.Detail) ? "" : $" ({Shorten(run.Detail!)})")}")
            .ToList();
        var text = string.Join("; ", reasons.Take(5));
        if (reasons.Count > 5)
        {
            text += $"; +{reasons.Count - 5} more";
        }

        return $"No search source available: {text}.";
    }

    private static string Shorten(string detail) => detail.Length <= 90 ? detail : detail[..87] + "...";
}

/// <summary>
/// The per-search request cap stopped a request (07d GOV-03 B): the site already got
/// <see cref="IndexerPolicy.MaxRequestsPerSearch"/> requests for this target, or its budget is low and only the
/// first query of each search is sent. The orchestrator keeps the rows found so far ("trimmed").
/// </summary>
public sealed class IndexerSearchCapException(string indexerId, int cap, bool lowBudget)
    : IndexerUnavailableException(indexerId, lowBudget
        ? $"daily budget low: {cap} request(s) per search"
        : $"per-search cap of {cap} request(s) reached")
{
    public bool LowBudget { get; } = lowBudget;
}
