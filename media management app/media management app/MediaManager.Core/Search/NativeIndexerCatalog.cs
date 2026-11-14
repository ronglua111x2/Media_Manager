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
    /// <summary>
    /// Legacy (before per-recipe backends): "Qbittorrent", "Native" or "Both". Read once by
    /// <see cref="SearchBackendMigration"/>; kept in settings.json so an older version still reads it. Never decides a search.
    /// </summary>
    public string Backend { get; set; } = SearchBackends.Qbittorrent;

    /// <summary>Legacy per media kind override of <see cref="Backend"/>. Read once by the migration, like <see cref="Backend"/>.</summary>
    public Dictionary<string, string> BackendByKind { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, IndexerSettings> Indexers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Timeout of each network request to an indexer (a hanging site is cut off).</summary>
    public int PerIndexerTimeoutSeconds { get; set; } = 15;

    /// <summary>Budget of one interactive search, including waiting for a site's rate limit (Auto-Track gets 5 min).</summary>
    public int OverallTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Time one site may take in one search the user started (all its queries, pages and detail lookups, and waiting
    /// for its turn), separate from the per-request timeout: a site over it is dropped from that search, the others
    /// do not wait for it (<see cref="SearchBudget.PerSite"/>).
    /// </summary>
    public int SiteBudgetSeconds { get; set; } = 15;

    /// <summary>The same for Auto-Track and other unattended searches, which can afford to wait longer.</summary>
    public int BackgroundSiteBudgetSeconds { get; set; } = 60;

    public bool PauseAll { get; set; }

    /// <summary>
    /// Master switch of native search. Off: every recipe searches with qBittorrent; a recipe set to Native/Both does so
    /// with a warning in the log and on the order. Each recipe chooses Qbittorrent, Native or Both.
    /// </summary>
    public bool NativeEnabled { get; set; }

    /// <summary>True once the legacy <see cref="Backend"/>/<see cref="BackendByKind"/> were moved into the recipes.</summary>
    public bool RecipeBackendsMigrated { get; set; }
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
        "sktorrent", "kickass", "nyaa", "tsukihime", "subsplease", "nekobt", "animetosho"
    ];

    /// <summary>
    /// Off unless the user enables them: AnimeTosho stopped updating (archive, newest entry May 2026);
    /// Kickass search pages carry no info-hash or magnet (only detail pages do), so its rows cannot be added
    /// until a lazy "resolve magnet of the chosen candidate" step exists.
    /// </summary>
    public static IReadOnlySet<string> DisabledByDefault { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "animetosho", "kickass" };

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
                "sktorrent" => new SkTorrentIndexer(config.BaseUrl ?? "https://sktorrent.eu"),
                "kickass" => new KickassIndexer(config.BaseUrl ?? "https://katcr.to"),
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
