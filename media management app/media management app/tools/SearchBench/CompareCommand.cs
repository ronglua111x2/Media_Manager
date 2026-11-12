using System.Diagnostics;
using System.Text.Json;
using media_management_app.Common;
using media_management_app.Search;
using media_management_app.Services;

namespace SearchBench;

/// <summary>
/// SearchBench compare [--titles ../SearchProbe/special_titles.json] [--plugins enabled] [--max-seconds 60]
///
/// Same queries on the native indexers (governed, like the app) and on qBittorrent's search.
/// Prints, per query: unique info-hashes/URLs, overlap, time, and per-indexer status. This is the
/// measurement used for the native-search cutover gate (docs/planning/native-search/06 §4).
/// </summary>
public static class CompareCommand
{
    private sealed record TitleCase(string Id, string Query, string Note);

    public static async Task<int> RunAsync(Dictionary<string, string> args)
    {
        var titlesPath = args.GetValueOrDefault("titles")
                         ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SearchProbe", "special_titles.json");
        var plugins = args.GetValueOrDefault("plugins") ?? "enabled";
        var maxSeconds = int.TryParse(args.GetValueOrDefault("max-seconds"), out var seconds) ? seconds : 60;
        var cases = JsonSerializer.Deserialize<List<TitleCase>>(File.ReadAllText(titlesPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        var governor = new IndexerRequestGovernor();
        var cache = new IndexerResponseCache();
        var orchestrator = new SearchOrchestrator(governor, (id, lane, budget) =>
            new GovernedIndexerHttp(http, governor, cache, id, lane, budget.MaxGovernorWait, TimeSpan.FromMinutes(30), "MediaManager/1.0 (+native-search; SearchBench)", budget.PerIndexer));
        var indexers = NativeIndexerCatalog.Create();
        using var qbit = new QbittorrentSearchBackend();
        await qbit.InitAsync(CancellationToken.None);

        Console.WriteLine("| Case | Query | Native unique | qBittorrent unique | Both | Native only | qBit only | Native s | qBit s | Native indexers |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var item in cases)
        {
            var anime = item.Id.Contains("anime", StringComparison.OrdinalIgnoreCase) || item.Id.Contains("romaji", StringComparison.OrdinalIgnoreCase);
            var kind = item.Id.StartsWith("tv", StringComparison.OrdinalIgnoreCase) ? MediaKind.TvSeasonPack
                : item.Id.StartsWith("anime", StringComparison.OrdinalIgnoreCase) ? MediaKind.TvEpisode
                : MediaKind.Movie;

            var nativeWatch = Stopwatch.StartNew();
            var native = await orchestrator.SearchAsync([new IndexerQuery(item.Query, kind, anime)], indexers, SearchLane.Interactive, SearchBudget.Interactive, CancellationToken.None);
            nativeWatch.Stop();

            var qbitRun = await qbit.SearchAsync(item.Query, plugins, maxSeconds, CancellationToken.None);

            var nativeKeys = native.Results.Select(r => CandidateDeduplicator.IdentityKey(r.FileUrl)).ToHashSet();
            var qbitKeys = qbitRun.Rows.Where(r => !string.IsNullOrWhiteSpace(r.FileUrl)).Select(r => CandidateDeduplicator.IdentityKey(r.FileUrl)).ToHashSet();
            var indexerSummary = string.Join(", ", native.Indexers.Select(i => $"{i.IndexerId} {i.Status} {i.Rows}"));
            Console.WriteLine($"| {item.Id} | {item.Query} | {nativeKeys.Count} | {qbitKeys.Count} | {nativeKeys.Intersect(qbitKeys).Count()} | " +
                              $"{nativeKeys.Except(qbitKeys).Count()} | {qbitKeys.Except(nativeKeys).Count()} | {nativeWatch.Elapsed.TotalSeconds:0.0} | {qbitRun.Seconds:0.0} | {indexerSummary} |");
        }

        Console.WriteLine();
        Console.WriteLine("Governor state after run:");
        foreach (var state in governor.Snapshot())
        {
            Console.WriteLine($"- {state.IndexerId}: requests={state.RequestsToday} breaker={state.Breaker} lastError={state.LastError ?? "-"}");
        }

        return 0;
    }
}
