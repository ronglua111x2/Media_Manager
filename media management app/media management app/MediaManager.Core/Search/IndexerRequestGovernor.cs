namespace media_management_app.Search;

public enum SearchLane
{
    /// <summary>The user pressed search (cart, recipe dry-run): served first.</summary>
    Interactive,

    /// <summary>Auto-Track and other unattended work: only uses part of the daily budget.</summary>
    Background
}

public enum IndexerBreakerState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>Per-site request policy. Defaults are deliberately conservative: free sites ban IPs fast.</summary>
public sealed record IndexerPolicy(
    int RequestsPerMinute,
    int MaxConcurrent,
    TimeSpan MinInterval,
    int DailyBudget,
    double BackgroundBudgetShare = 0.7)
{
    public static IndexerPolicy Conservative { get; } = new(6, 1, TimeSpan.FromSeconds(5), 200);

    /// <summary>Defaults from docs/planning/native-search/03-request-governor.md §2.</summary>
    public static IndexerPolicy DefaultFor(string indexerId) => indexerId switch
    {
        "knaben" => new IndexerPolicy(10, 1, TimeSpan.FromSeconds(2), 500),
        "apibay" => new IndexerPolicy(15, 2, TimeSpan.FromSeconds(1), 800),
        "nyaa" => new IndexerPolicy(10, 1, TimeSpan.FromSeconds(2), 600),
        "limetorrents" => new IndexerPolicy(8, 1, TimeSpan.FromSeconds(4), 400),
        "eztv" => new IndexerPolicy(10, 1, TimeSpan.FromSeconds(2), 300),
        _ => Conservative
    };

    /// <summary>User settings may only make a policy stricter, never looser than the default.</summary>
    public IndexerPolicy RestrictTo(int? requestsPerMinute, int? dailyBudget) => this with
    {
        RequestsPerMinute = requestsPerMinute is > 0 ? Math.Min(RequestsPerMinute, requestsPerMinute.Value) : RequestsPerMinute,
        DailyBudget = dailyBudget is > 0 ? Math.Min(DailyBudget, dailyBudget.Value) : DailyBudget
    };
}

/// <summary>Persisted per-indexer state (survives app restarts so limits are not reset by a restart).</summary>
public sealed class IndexerStateSnapshot
{
    public string IndexerId { get; set; } = string.Empty;

    public string DayKey { get; set; } = string.Empty;

    public int RequestsToday { get; set; }

    public IndexerBreakerState Breaker { get; set; }

    public DateTimeOffset? OpenUntilUtc { get; set; }

    public DateTimeOffset? CooldownUntilUtc { get; set; }

    public int ConsecutiveFailures { get; set; }

    public int OpenCount { get; set; }

    public int RateLimitLevel { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? LastErrorUtc { get; set; }

    public DateTimeOffset? LastSuccessUtc { get; set; }

    public int InFlight { get; set; }

    public IndexerStateSnapshot Clone() => (IndexerStateSnapshot)MemberwiseClone();
}

public readonly record struct IndexerAvailability(bool CanRun, string? Reason);

/// <summary>
/// Central request governor (native-search P2, user requirement: free sites ban IPs that hammer them).
/// Every indexer request must acquire a lease here. Enforces per-site rate (requests/minute, minimum
/// interval, concurrency), daily budget with a reduced share for the background lane, honours 429
/// Retry-After with exponential backoff, and opens a circuit breaker on blocks (403/challenge) or
/// repeated failures. Never tries to get around a block. Pure logic over a <see cref="TimeProvider"/>.
/// </summary>
public sealed class IndexerRequestGovernor
{
    private static readonly TimeSpan[] BlockedOpenDurations =
        [TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24)];

    private static readonly TimeSpan[] FailureOpenDurations =
        [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(2)];

    private const int FailuresBeforeOpen = 3;

    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly Func<string, IndexerPolicy> _policyFor;
    private readonly Dictionary<string, IndexerStateSnapshot> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastStart = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
    private bool _killSwitch;

    public IndexerRequestGovernor(
        TimeProvider? time = null,
        Func<string, IndexerPolicy>? policyFor = null,
        IEnumerable<IndexerStateSnapshot>? restored = null)
    {
        _time = time ?? TimeProvider.System;
        _policyFor = policyFor ?? IndexerPolicy.DefaultFor;
        foreach (var snapshot in restored ?? [])
        {
            var copy = snapshot.Clone();
            copy.InFlight = 0;
            _states[copy.IndexerId] = copy;
        }
    }

    /// <summary>Raised after any state change worth persisting.</summary>
    public event Action? StateChanged;

    public bool KillSwitch
    {
        get { lock (_gate) { return _killSwitch; } }
        set { lock (_gate) { _killSwitch = value; } StateChanged?.Invoke(); }
    }

    public void SetDisabled(string indexerId, bool disabled)
    {
        lock (_gate)
        {
            if (disabled)
            {
                _disabled.Add(indexerId);
            }
            else
            {
                _disabled.Remove(indexerId);
            }
        }
    }

    public IndexerAvailability GetAvailability(string indexerId, SearchLane lane = SearchLane.Interactive)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var reason = BlockingReason(indexerId, lane, now, out _);
            return new IndexerAvailability(reason is null, reason);
        }
    }

    public IReadOnlyList<IndexerStateSnapshot> Snapshot()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            return _states.Values.Select(state =>
            {
                RollDay(state, now);
                return state.Clone();
            }).OrderBy(state => state.IndexerId, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>
    /// Waits until a request to <paramref name="indexerId"/> is allowed, then returns a lease to dispose
    /// when the request ends. Throws <see cref="IndexerUnavailableException"/> when it cannot run within
    /// <paramref name="maxWait"/> (breaker open, budget used, kill switch, long cooldown).
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string indexerId, SearchLane lane, TimeSpan maxWait, CancellationToken cancellationToken)
    {
        var deadline = _time.GetUtcNow() + maxWait;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan wait;
            lock (_gate)
            {
                var now = _time.GetUtcNow();
                var reason = BlockingReason(indexerId, lane, now, out var retryAt);
                if (reason is not null && retryAt is null)
                {
                    throw new IndexerUnavailableException(indexerId, reason);
                }

                if (reason is null)
                {
                    var state = StateFor(indexerId);
                    var recent = RecentFor(indexerId);
                    recent.Enqueue(now);
                    _lastStart[indexerId] = now;
                    state.RequestsToday++;
                    state.InFlight++;
                    if (state.Breaker == IndexerBreakerState.Open)
                    {
                        state.Breaker = IndexerBreakerState.HalfOpen;
                    }

                    return new Lease(this, indexerId);
                }

                if (retryAt!.Value > deadline)
                {
                    throw new IndexerUnavailableException(indexerId, $"{reason} (would wait past the search budget)");
                }

                wait = retryAt.Value - now;
            }

            await Task.Delay(wait < TimeSpan.FromMilliseconds(10) ? TimeSpan.FromMilliseconds(10) : wait, _time, cancellationToken);
        }
    }

    /// <summary>Records the outcome of a request (call once per request, after the lease was acquired).</summary>
    public void Report(string indexerId, IndexerFailureKind outcome, TimeSpan? retryAfter = null, string? detail = null)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var state = StateFor(indexerId);
            switch (outcome)
            {
                case IndexerFailureKind.None:
                    state.ConsecutiveFailures = 0;
                    state.RateLimitLevel = 0;
                    state.Breaker = IndexerBreakerState.Closed;
                    state.OpenUntilUtc = null;
                    state.LastSuccessUtc = now;
                    break;

                case IndexerFailureKind.RateLimited:
                    var backoff = retryAfter ?? TimeSpan.FromSeconds(30 * Math.Pow(2, Math.Min(state.RateLimitLevel, 6)));
                    if (backoff > TimeSpan.FromMinutes(30))
                    {
                        backoff = TimeSpan.FromMinutes(30);
                    }

                    state.RateLimitLevel++;
                    state.CooldownUntilUtc = now + backoff;
                    Fail(state, now, $"rate limited ({detail}); cooling down {backoff.TotalSeconds:0}s");
                    break;

                case IndexerFailureKind.Blocked:
                    Open(state, now, BlockedOpenDurations, $"blocked ({detail})");
                    break;

                default:
                    state.ConsecutiveFailures++;
                    Fail(state, now, $"{outcome} ({detail})");
                    if (state.Breaker == IndexerBreakerState.HalfOpen || state.ConsecutiveFailures >= FailuresBeforeOpen)
                    {
                        Open(state, now, FailureOpenDurations, $"{state.ConsecutiveFailures} failures, last {outcome}");
                    }

                    break;
            }
        }

        StateChanged?.Invoke();
    }

    private void Release(string indexerId)
    {
        lock (_gate)
        {
            var state = StateFor(indexerId);
            state.InFlight = Math.Max(0, state.InFlight - 1);
        }
    }

    private string? BlockingReason(string indexerId, SearchLane lane, DateTimeOffset now, out DateTimeOffset? retryAt)
    {
        retryAt = null;
        if (_killSwitch)
        {
            return "all searches paused (kill switch)";
        }

        if (_disabled.Contains(indexerId))
        {
            return "disabled";
        }

        var policy = _policyFor(indexerId);
        var state = StateFor(indexerId);
        RollDay(state, now);

        if (state.Breaker == IndexerBreakerState.Open && state.OpenUntilUtc is { } openUntil && openUntil > now)
        {
            return $"breaker open until {openUntil:HH:mm} UTC: {state.LastError}";
        }

        if (state.Breaker == IndexerBreakerState.HalfOpen && state.InFlight > 0)
        {
            retryAt = now + TimeSpan.FromSeconds(1);
            return "half-open probe in progress";
        }

        var budget = lane == SearchLane.Background
            ? (int)Math.Floor(policy.DailyBudget * policy.BackgroundBudgetShare)
            : policy.DailyBudget;
        if (state.RequestsToday >= budget)
        {
            return $"daily budget used ({state.RequestsToday}/{budget}, {lane})";
        }

        if (state.CooldownUntilUtc is { } cooldown && cooldown > now)
        {
            retryAt = cooldown;
            return $"cooling down after rate limit until {cooldown:HH:mm:ss} UTC";
        }

        if (state.InFlight >= policy.MaxConcurrent)
        {
            retryAt = now + TimeSpan.FromMilliseconds(250);
            return "max concurrent requests";
        }

        if (_lastStart.TryGetValue(indexerId, out var last) && now - last < policy.MinInterval)
        {
            retryAt = last + policy.MinInterval;
            return "min interval";
        }

        var recent = RecentFor(indexerId);
        while (recent.Count > 0 && now - recent.Peek() >= TimeSpan.FromMinutes(1))
        {
            recent.Dequeue();
        }

        if (recent.Count >= policy.RequestsPerMinute)
        {
            retryAt = recent.Peek() + TimeSpan.FromMinutes(1);
            return "requests per minute";
        }

        return null;
    }

    private static void Fail(IndexerStateSnapshot state, DateTimeOffset now, string detail)
    {
        state.LastError = detail;
        state.LastErrorUtc = now;
    }

    private static void Open(IndexerStateSnapshot state, DateTimeOffset now, TimeSpan[] durations, string detail)
    {
        var duration = durations[Math.Min(state.OpenCount, durations.Length - 1)];
        state.OpenCount++;
        state.Breaker = IndexerBreakerState.Open;
        state.OpenUntilUtc = now + duration;
        state.ConsecutiveFailures = 0;
        Fail(state, now, detail);
    }

    private static void RollDay(IndexerStateSnapshot state, DateTimeOffset now)
    {
        var day = now.UtcDateTime.ToString("yyyy-MM-dd");
        if (!string.Equals(state.DayKey, day, StringComparison.Ordinal))
        {
            state.DayKey = day;
            state.RequestsToday = 0;
            // A new day forgives the escalation of past blocks, not a breaker that is still open.
            if (state.Breaker == IndexerBreakerState.Closed)
            {
                state.OpenCount = 0;
            }
        }
    }

    private IndexerStateSnapshot StateFor(string indexerId)
    {
        if (!_states.TryGetValue(indexerId, out var state))
        {
            state = new IndexerStateSnapshot { IndexerId = indexerId };
            _states[indexerId] = state;
        }

        return state;
    }

    private Queue<DateTimeOffset> RecentFor(string indexerId)
    {
        if (!_recent.TryGetValue(indexerId, out var queue))
        {
            queue = new Queue<DateTimeOffset>();
            _recent[indexerId] = queue;
        }

        return queue;
    }

    private sealed class Lease(IndexerRequestGovernor owner, string indexerId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(indexerId);
            }
        }
    }
}
