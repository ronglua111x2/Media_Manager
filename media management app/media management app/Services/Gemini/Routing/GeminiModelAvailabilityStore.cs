using System.Text.Json;
using System.Text.Json.Serialization;
using media_management_app.Common;

namespace media_management_app.Services.Gemini.Routing;

public enum GeminiModelLockReason
{
    None,
    DailyQuota,
    MinuteQuota,
    Overloaded,
    ServerError,
    NoFreeTier,
    ModelGone,
    Incompatible
}

/// <summary>What the router knows about one model right now.</summary>
public sealed record GeminiModelSnapshot(
    string Model,
    int RequestsToday,
    int? DailyLimit,
    GeminiModelLockReason LockReason,
    DateTimeOffset? LockedUntilUtc,
    string? LockDetail,
    DateTimeOffset? LastRequestUtc)
{
    public bool IsPermanentlyLocked => LockReason is GeminiModelLockReason.NoFreeTier
        or GeminiModelLockReason.ModelGone
        or GeminiModelLockReason.Incompatible;

    public int? RemainingToday => DailyLimit is { } limit ? Math.Max(0, limit - RequestsToday) : null;
}

/// <summary>
/// Per-model request counts and locks for the Gemini router, kept in the state folder.
/// Quotas are per Google Cloud project and per model; the day starts at midnight Pacific time, as on Google's side.
/// </summary>
/// <remarks>
/// Lock handling follows ModelAvailabilityService in google-gemini/gemini-cli (Apache-2.0), with two changes for a
/// long-running app: quota locks expire (at the time Google gives in retryDelay, else the next Pacific midnight),
/// and the state survives restarts.
/// </remarks>
public sealed class GeminiModelAvailabilityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly TimeZoneInfo PacificTimeZone = ResolvePacificTimeZone();

    private readonly ISettingsService _settingsService;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private RouterState? _state;
    private string? _loadedPath;

    public GeminiModelAvailabilityStore(ISettingsService settingsService, TimeProvider timeProvider)
    {
        _settingsService = settingsService;
        _timeProvider = timeProvider;
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <summary>Start of the next quota day (midnight Pacific time) after <paramref name="utcNow"/>.</summary>
    public static DateTimeOffset NextPacificMidnight(DateTimeOffset utcNow)
    {
        var pacificNow = TimeZoneInfo.ConvertTime(utcNow, PacificTimeZone);
        var nextMidnightLocal = pacificNow.Date.AddDays(1);
        var offset = PacificTimeZone.GetUtcOffset(nextMidnightLocal);
        return new DateTimeOffset(nextMidnightLocal, offset).ToUniversalTime();
    }

    public static string PacificDayKey(DateTimeOffset utcNow) =>
        TimeZoneInfo.ConvertTime(utcNow, PacificTimeZone).ToString("yyyy-MM-dd");

    public GeminiModelSnapshot GetSnapshot(string model, int? catalogDailyLimit)
    {
        lock (_sync)
        {
            var entry = GetEntry(model, create: false);
            var now = UtcNow;
            if (entry is null)
            {
                return new GeminiModelSnapshot(model, 0, catalogDailyLimit, GeminiModelLockReason.None, null, null, null);
            }

            RollDay(entry, now);
            var lockReason = entry.LockReason;
            var lockedUntil = entry.LockedUntilUtc;
            if (lockReason != GeminiModelLockReason.None && lockedUntil is { } until && until <= now)
            {
                lockReason = GeminiModelLockReason.None;
                lockedUntil = null;
            }

            return new GeminiModelSnapshot(
                model,
                entry.RequestsToday,
                entry.LearnedDailyLimit ?? catalogDailyLimit,
                lockReason,
                lockedUntil,
                lockReason == GeminiModelLockReason.None ? null : entry.LockDetail,
                entry.LastRequestUtc);
        }
    }

    /// <summary>Counts a request before it is sent. Google counts failed requests too.</summary>
    public void RecordAttempt(string model)
    {
        lock (_sync)
        {
            var entry = GetEntry(model, create: true)!;
            var now = UtcNow;
            RollDay(entry, now);
            entry.RequestsToday++;
            entry.LastRequestUtc = now;
            Save();
        }
    }

    /// <param name="until">Null locks until the catalog is refreshed (no free tier, model gone, incompatible).</param>
    public void Lock(string model, GeminiModelLockReason reason, DateTimeOffset? until, string? detail)
    {
        lock (_sync)
        {
            var entry = GetEntry(model, create: true)!;
            entry.LockReason = reason;
            entry.LockedUntilUtc = until;
            entry.LockDetail = detail is { Length: > 300 } ? detail[..300] + "..." : detail;
            Save();
        }
    }

    public void LearnDailyLimit(string model, int limit)
    {
        if (limit <= 0)
        {
            return;
        }

        lock (_sync)
        {
            var entry = GetEntry(model, create: true)!;
            entry.LearnedDailyLimit = limit;
            Save();
        }
    }

    /// <summary>Clears locks that only a catalog refresh may lift (no free tier, model gone, incompatible).</summary>
    public void ClearPermanentLocks()
    {
        lock (_sync)
        {
            var state = Load();
            foreach (var entry in state.Models.Values)
            {
                if (entry.LockReason is GeminiModelLockReason.NoFreeTier
                    or GeminiModelLockReason.ModelGone
                    or GeminiModelLockReason.Incompatible)
                {
                    entry.LockReason = GeminiModelLockReason.None;
                    entry.LockedUntilUtc = null;
                    entry.LockDetail = null;
                }
            }

            Save();
        }
    }

    /// <summary>Time to wait so requests to <paramref name="model"/> keep within its per-minute limit.</summary>
    public TimeSpan GetSpacingWait(string model, int? requestsPerMinute)
    {
        if (requestsPerMinute is not > 0)
        {
            return TimeSpan.Zero;
        }

        lock (_sync)
        {
            var entry = GetEntry(model, create: false);
            if (entry?.LastRequestUtc is not { } last)
            {
                return TimeSpan.Zero;
            }

            var spacing = TimeSpan.FromSeconds(60.0 / requestsPerMinute.Value);
            var wait = last + spacing - UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
    }

    private ModelState? GetEntry(string model, bool create)
    {
        var state = Load();
        if (state.Models.TryGetValue(model, out var entry))
        {
            return entry;
        }

        if (!create)
        {
            return null;
        }

        entry = new ModelState();
        state.Models[model] = entry;
        return entry;
    }

    private static void RollDay(ModelState entry, DateTimeOffset now)
    {
        var today = PacificDayKey(now);
        if (string.Equals(entry.DayKey, today, StringComparison.Ordinal))
        {
            return;
        }

        entry.DayKey = today;
        entry.RequestsToday = 0;
        if (entry.LockReason == GeminiModelLockReason.DailyQuota)
        {
            entry.LockReason = GeminiModelLockReason.None;
            entry.LockedUntilUtc = null;
            entry.LockDetail = null;
        }
    }

    private RouterState Load()
    {
        var path = GetStateFilePath();
        if (_state is not null && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return _state;
        }

        _loadedPath = path;
        try
        {
            _state = File.Exists(path)
                ? JsonSerializer.Deserialize<RouterState>(File.ReadAllText(path), JsonOptions) ?? new RouterState()
                : new RouterState();
        }
        catch
        {
            _state = new RouterState();
        }

        _state.Models = new Dictionary<string, ModelState>(_state.Models, StringComparer.OrdinalIgnoreCase);
        return _state;
    }

    private void Save()
    {
        if (_state is null)
        {
            return;
        }

        try
        {
            var path = GetStateFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(_state, JsonOptions));
        }
        catch
        {
            // Router state is a cache of what Google already enforces; failing to save must not block linking.
        }
    }

    private string GetStateFilePath() =>
        Path.Combine(_settingsService.Current.StateFolder, AppConstants.GeminiRouterStateFileName);

    private static TimeZoneInfo ResolvePacificTimeZone()
    {
        foreach (var id in new[] { "Pacific Standard Time", "America/Los_Angeles" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Pacific (fixed -8)", TimeSpan.FromHours(-8), "Pacific", "Pacific");
    }

    private sealed class RouterState
    {
        public Dictionary<string, ModelState> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ModelState
    {
        public string DayKey { get; set; } = string.Empty;

        public int RequestsToday { get; set; }

        public int? LearnedDailyLimit { get; set; }

        public DateTimeOffset? LastRequestUtc { get; set; }

        public GeminiModelLockReason LockReason { get; set; }

        public DateTimeOffset? LockedUntilUtc { get; set; }

        public string? LockDetail { get; set; }
    }
}
