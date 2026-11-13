using System.Text.Json;
using media_management_app.Common;
using media_management_app.Services.Gemini.Routing;

namespace GeminiSpecialEval.Infrastructure;

/// <summary>Which daily cap the eval applies to a model.</summary>
internal enum QuotaClass
{
    /// <summary>Not in the app's catalog with a known free limit: the eval refuses it.</summary>
    Unknown,

    /// <summary>500 requests/day on the free tier (Flash-Lite): all such models share one eval cap.</summary>
    Large,

    /// <summary>20 requests/day or fewer on the free tier: a small cap per model.</summary>
    Small
}

/// <summary>
/// The eval's own request counter, per model and Pacific day (Google's quota day), kept in the data folder.
/// Every request is reserved and written to disk before it is sent, so failed, timed-out and crashed requests
/// count too. The caps are hard limits of the harness; the app shares the same key and project quota.
/// </summary>
internal sealed class QuotaLedger
{
    /// <summary>Total per Pacific day over every 500/day model together.</summary>
    public const int LargeSharedCap = 100;

    /// <summary>Per Pacific day on each model whose free limit is 20/day or less.</summary>
    public const int SmallPerModelCap = 5;

    private readonly string _path;
    private readonly int? _runCap;
    private readonly Func<DateTimeOffset> _utcNow;
    private int _runSpent;

    public QuotaLedger(string dataDir, int? runCap = null, Func<DateTimeOffset>? utcNow = null)
    {
        _path = Path.Combine(dataDir, "quota.json");
        _runCap = runCap;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string PacificDay => GeminiModelAvailabilityStore.PacificDayKey(_utcNow());

    public int RunSpent => _runSpent;

    public static QuotaClass Classify(string model)
    {
        model = Running.ModelSpec.IdOf(model);
        var entry = GeminiModelCatalogDefaults.CreateSeed().Models
            .FirstOrDefault(item => string.Equals(item.Id, model, StringComparison.OrdinalIgnoreCase));
        return entry?.FreeRequestsPerDay switch
        {
            >= 500 => QuotaClass.Large,
            > 0 and <= 20 => QuotaClass.Small,
            _ => QuotaClass.Unknown
        };
    }

    /// <summary>Requests this model may still send today under the eval's caps (and this run's cap).</summary>
    public int Remaining(string model)
    {
        model = Running.ModelSpec.IdOf(model);
        var file = Load();
        var day = PacificDay;
        var remaining = Classify(model) switch
        {
            QuotaClass.Large => LargeSharedCap - file.Entries.Count(entry => entry.PacificDay == day && Classify(entry.Model) == QuotaClass.Large),
            QuotaClass.Small => SmallPerModelCap - UsedToday(file, day, model),
            _ => 0
        };
        if (_runCap is { } cap)
        {
            remaining = Math.Min(remaining, cap - _runSpent);
        }

        return Math.Max(0, remaining);
    }

    /// <summary>Remaining requests of the shared 500/day bucket today (ignoring this run's cap).</summary>
    public int LargeRemaining()
    {
        var file = Load();
        var day = PacificDay;
        return Math.Max(0, LargeSharedCap - file.Entries.Count(entry => entry.PacificDay == day && Classify(entry.Model) == QuotaClass.Large));
    }

    public IReadOnlyDictionary<string, int> UsedTodayByModel()
    {
        var file = Load();
        var day = PacificDay;
        return file.Entries
            .Where(entry => entry.PacificDay == day)
            .GroupBy(entry => entry.Model, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Counts one request before it is sent. False (nothing sent) when a cap would be exceeded.</summary>
    public bool TryReserve(string model, string label, out string entryId)
    {
        model = Running.ModelSpec.IdOf(model);
        entryId = string.Empty;
        if (Remaining(model) < 1)
        {
            return false;
        }

        var file = Load();
        var now = _utcNow();
        entryId = Guid.NewGuid().ToString("N")[..12];
        file.Entries.Add(new LedgerEntry
        {
            Id = entryId,
            Utc = now,
            PacificDay = GeminiModelAvailabilityStore.PacificDayKey(now),
            Model = model,
            Label = label
        });
        Save(file);
        _runSpent++;
        return true;
    }

    public void Complete(string entryId, int? status, string? error)
    {
        var file = Load();
        var entry = file.Entries.FirstOrDefault(item => item.Id == entryId);
        if (entry is null)
        {
            return;
        }

        entry.Status = status;
        entry.Error = error;
        Save(file);
    }

    private static int UsedToday(LedgerFile file, string day, string model) =>
        file.Entries.Count(entry => entry.PacificDay == day && string.Equals(entry.Model, model, StringComparison.OrdinalIgnoreCase));

    private LedgerFile Load()
    {
        if (!File.Exists(_path))
        {
            return new LedgerFile();
        }

        // A ledger that cannot be read must stop the run rather than reset the count to zero.
        return JsonSerializer.Deserialize<LedgerFile>(File.ReadAllText(_path), Json.ReadOptions)
               ?? throw new InvalidDataException($"Quota ledger {_path} is empty or unreadable; fix or remove it by hand.");
    }

    private void Save(LedgerFile file) => SafeFile.WriteAllText(_path, JsonSerializer.Serialize(file, Json.WriteOptions));

    private sealed class LedgerFile
    {
        public int Version { get; set; } = 1;

        public List<LedgerEntry> Entries { get; set; } = [];
    }

    private sealed class LedgerEntry
    {
        public string Id { get; set; } = string.Empty;

        public DateTimeOffset Utc { get; set; }

        public string PacificDay { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public int? Status { get; set; }

        public string? Error { get; set; }
    }
}
