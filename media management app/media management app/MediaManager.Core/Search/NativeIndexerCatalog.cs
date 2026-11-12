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

    /// <summary>Timeout of each network request to an indexer (a hanging site is cut off).</summary>
    public int PerIndexerTimeoutSeconds { get; set; } = 15;

    /// <summary>Budget of one interactive search, including waiting for a site's rate limit (Auto-Track gets 5 min).</summary>
    public int OverallTimeoutSeconds { get; set; } = 90;

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
    public static IReadOnlyList<string> AllIds { get; } =
    [
        "knaben", "apibay", "limetorrents", "eztv", "yts", "torrents-csv", "bitsearch", "bt4g", "torrentdownloads",
        "nyaa", "tsukihime", "subsplease", "nekobt", "animetosho"
    ];

    /// <summary>
    /// Off unless the user enables them: AnimeTosho stopped updating (archive, newest entry May 2026).
    /// </summary>
    public static IReadOnlySet<string> DisabledByDefault { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "animetosho" };

    public static bool IsEnabled(NativeSearchSettings? settings, string id) =>
        settings?.Indexers.TryGetValue(id, out var config) == true ? config.Enabled : !DisabledByDefault.Contains(id);

    public static IReadOnlyList<ITorrentIndexer> Create(NativeSearchSettings? settings = null)
    {
        settings ??= new NativeSearchSettings();
        var indexers = new List<ITorrentIndexer>();
        foreach (var id in AllIds)
        {
            var config = settings.Indexers.TryGetValue(id, out var value) ? value : new IndexerSettings();
            if (!IsEnabled(settings, id))
            {
                continue;
            }

            indexers.Add(id switch
            {
                "knaben" => new KnabenIndexer(config.BaseUrl ?? "https://api.knaben.org"),
                "apibay" => new ApibayIndexer(config.BaseUrl ?? "https://apibay.org"),
                "nyaa" => new NyaaIndexer(config.BaseUrl ?? "https://nyaa.si"),
                "limetorrents" => new LimeTorrentsIndexer(config.BaseUrl ?? "https://www.limetorrents.fun"),
                "eztv" => new EztvIndexer(config.BaseUrl ?? "https://eztvx.to"),
                "yts" => new YtsIndexer(config.BaseUrl ?? "https://yts.gg"),
                "torrents-csv" => new TorrentsCsvIndexer(config.BaseUrl ?? "https://torrents-csv.com"),
                "bitsearch" => new BitsearchIndexer(config.BaseUrl ?? "https://bitsearch.eu"),
                "bt4g" => new Bt4gIndexer(config.BaseUrl ?? "https://bt4gprx.com"),
                "torrentdownloads" => new TorrentDownloadsIndexer(config.BaseUrl ?? "https://www.torrentdownloads.pro"),
                "tsukihime" => new TsukiHimeIndexer(config.BaseUrl ?? "https://api.tsukihime.org"),
                "subsplease" => new SubsPleaseIndexer(config.BaseUrl ?? "https://subsplease.org"),
                "nekobt" => new NekoBtIndexer(config.BaseUrl ?? "https://nekobt.to"),
                "animetosho" => new AnimeToshoIndexer(config.BaseUrl ?? "https://animetosho.org"),
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
