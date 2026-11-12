using media_management_app.Search.Indexers;

namespace media_management_app.Search;

/// <summary>Per-indexer user settings (settings.json → Search.Indexers). Limits may only be lowered.</summary>
public sealed class IndexerSettings
{
    public bool Enabled { get; set; } = true;

    public string? BaseUrl { get; set; }

    public int? RequestsPerMinute { get; set; }

    public int? DailyBudget { get; set; }
}

/// <summary>Native search settings (settings.json → Search).</summary>
public sealed class NativeSearchSettings
{
    /// <summary>"Qbittorrent" (default until the P6 cutover), "Native" or "Both" (native results + comparison log).</summary>
    public string Backend { get; set; } = SearchBackends.Qbittorrent;

    /// <summary>Per media kind override of <see cref="Backend"/>, e.g. {"Movie": "Native"} to roll out movies first.</summary>
    public Dictionary<string, string> BackendByKind { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, IndexerSettings> Indexers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int PerIndexerTimeoutSeconds { get; set; } = 15;

    public int OverallTimeoutSeconds { get; set; } = 30;

    public bool PauseAll { get; set; }
}

public static class SearchBackends
{
    public const string Qbittorrent = "Qbittorrent";
    public const string Native = "Native";
    public const string Both = "Both";
}

public static class NativeIndexerCatalog
{
    /// <summary>Ids of all native indexers, in the order they are tried/displayed.</summary>
    public static IReadOnlyList<string> AllIds { get; } = ["knaben", "apibay", "nyaa", "limetorrents"];

    public static IReadOnlyList<ITorrentIndexer> Create(NativeSearchSettings? settings = null)
    {
        settings ??= new NativeSearchSettings();
        var indexers = new List<ITorrentIndexer>();
        foreach (var id in AllIds)
        {
            var config = settings.Indexers.TryGetValue(id, out var value) ? value : new IndexerSettings();
            if (!config.Enabled)
            {
                continue;
            }

            indexers.Add(id switch
            {
                "knaben" => new KnabenIndexer(config.BaseUrl ?? "https://api.knaben.org"),
                "apibay" => new ApibayIndexer(config.BaseUrl ?? "https://apibay.org"),
                "nyaa" => new NyaaIndexer(config.BaseUrl ?? "https://nyaa.si"),
                "limetorrents" => new LimeTorrentsIndexer(config.BaseUrl ?? "https://www.limetorrents.fun"),
                _ => throw new InvalidOperationException($"Unknown indexer '{id}'.")
            });
        }

        return indexers;
    }

    public static Func<string, IndexerPolicy> PolicyResolver(NativeSearchSettings? settings) => id =>
    {
        var policy = IndexerPolicy.DefaultFor(id);
        return settings?.Indexers.TryGetValue(id, out var config) == true
            ? policy.RestrictTo(config.RequestsPerMinute, config.DailyBudget)
            : policy;
    };
}
