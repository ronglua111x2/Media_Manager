using System.Diagnostics;
using System.Text.Json;
using System.Xml;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.Search;

/// <summary>Converts native indexer rows into the app's <see cref="TorrentSearchResult"/> so the existing
/// evaluation, scoring, dedupe, cart and add-gate code is reused unchanged.</summary>
public static class IndexerResultNormalizer
{
    // Well-known public trackers appended to magnets built from a bare info-hash.
    private static readonly string[] PublicTrackers =
    [
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.stealth.si:80/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://exodus.desync.com:6969/announce",
        "udp://tracker.openbittorrent.com:6969/announce"
    ];

    public static string BuildMagnet(string infoHash, string? name)
    {
        var magnet = $"magnet:?xt=urn:btih:{infoHash.ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(name))
        {
            magnet += "&dn=" + Uri.EscapeDataString(name);
        }

        return magnet + string.Concat(PublicTrackers.Select(tracker => "&tr=" + Uri.EscapeDataString(tracker)));
    }

    /// <summary>
    /// Meta indexers (Knaben) report the original tracker per row; map it to the old qBittorrent plugin name so
    /// recipe engine priorities ("nyaasi", "thepiratebay"...) still apply to those rows.
    /// </summary>
    public static string EngineNameFor(IndexerResult row, string indexerEngineName)
    {
        var source = (row.Source ?? string.Empty).ToLowerInvariant();
        return source switch
        {
            _ when source.Contains("nyaa") => "nyaasi",
            _ when source.Contains("pirate bay") => "thepiratebay",
            _ when source.Contains("1337x") => "one337x",
            _ when source.Contains("yts") => "yts",
            _ when source.Contains("tokyo") => "tokyotoshokan",
            _ when source.Contains("eztv") => "eztvx",
            _ when source.Contains("limetorrents") => "limetorrents",
            _ when source.Contains("torrentdownloads") => "torrentdownloads",
            _ => indexerEngineName
        };
    }

    public static bool IsValidInfoHash(string? hash) =>
        hash is { Length: 40 } && hash.All(Uri.IsHexDigit) && hash.Any(character => character != '0');

    public static TorrentSearchResult ToSearchResult(IndexerResult row, string engineName)
    {
        var hash = IsValidInfoHash(row.InfoHash) ? row.InfoHash!.ToLowerInvariant() : CandidateDeduplicator.TryGetInfoHash(row.MagnetUri);
        var url = !string.IsNullOrWhiteSpace(row.MagnetUri)
            ? row.MagnetUri!
            : hash is not null
                ? BuildMagnet(hash, row.Title)
                : row.TorrentUrl ?? row.DetailsUrl ?? string.Empty;
        return new TorrentSearchResult
        {
            FileName = row.Title,
            FileSize = row.SizeBytes,
            FileUrl = url,
            DescriptionUrl = row.DetailsUrl ?? string.Empty,
            Seeders = Math.Max(0, row.Seeders),
            Leechers = Math.Max(0, row.Leechers),
            EngineName = engineName,
            SiteUrl = row.Source ?? row.IndexerId
        };
    }
}

public sealed record IndexerRunTelemetry(
    string IndexerId,
    string Status,
    int Rows,
    int Queries,
    long ElapsedMs,
    string? Detail);

public sealed record OrchestratedSearch(
    IReadOnlyList<TorrentSearchResult> Results,
    IReadOnlyList<IndexerRunTelemetry> Indexers,
    long ElapsedMs)
{
    public string Summary =>
        $"rows={Results.Count} in {ElapsedMs}ms; " +
        string.Join(", ", Indexers.Select(i => $"{i.IndexerId}:{i.Status}:{i.Rows}/{i.ElapsedMs}ms{(i.Detail is null ? "" : $" ({i.Detail})")}"));
}

/// <summary>
/// Time budget of one search. PerIndexer limits each network request (a hanging site is cut off);
/// Overall limits the whole search including waiting for the request governor: when a site's rate limit is
/// used up, the search waits its turn instead of skipping the site.
/// </summary>
public sealed record SearchBudget(TimeSpan PerIndexer, TimeSpan Overall, TimeSpan MaxGovernorWait)
{
    public static SearchBudget Interactive { get; } = new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(90));

    public static SearchBudget Background { get; } = new(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
}

/// <summary>
/// Runs a set of queries over the eligible native indexers in parallel (each indexer runs its queries
/// in order, paced by the governor), with a per-indexer and an overall time budget. One slow or broken
/// site can no longer hold the whole search hostage (audit SRCH-01: qBittorrent jobs always ran 180 s).
/// </summary>
public sealed class SearchOrchestrator(IndexerRequestGovernor governor, Func<string, SearchLane, SearchBudget, IIndexerHttp> httpFactory)
{
    public static IndexerMediaKinds KindsFor(MediaKind kind, bool isAnime) => kind switch
    {
        MediaKind.Movie => IndexerMediaKinds.Movie,
        MediaKind.TvSeasonPack => IndexerMediaKinds.TvSeasonPack,
        _ => IndexerMediaKinds.TvEpisode
    } | (isAnime ? IndexerMediaKinds.Anime : IndexerMediaKinds.None);

    public async Task<OrchestratedSearch> SearchAsync(
        IReadOnlyList<IndexerQuery> queries,
        IReadOnlyList<ITorrentIndexer> indexers,
        SearchLane lane,
        SearchBudget budget,
        CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        var distinct = queries
            .Where(query => !string.IsNullOrWhiteSpace(query.Text) || query.ImdbId is not null)
            .DistinctBy(query => (query.Text.ToLowerInvariant(), query.ImdbId))
            .ToList();
        var telemetry = new List<IndexerRunTelemetry>();
        if (distinct.Count == 0)
        {
            return new OrchestratedSearch([], telemetry, 0);
        }

        var wanted = KindsFor(distinct[0].Kind, distinct[0].IsAnime);
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(budget.Overall);

        var runs = new List<Task<(IndexerRunTelemetry Telemetry, List<TorrentSearchResult> Rows)>>();
        foreach (var indexer in indexers)
        {
            // An anime query also goes to general indexers; a non-anime query skips anime-only ones.
            var serves = (indexer.Capabilities.Kinds & wanted & ~IndexerMediaKinds.Anime) != 0 ||
                         (wanted.HasFlag(IndexerMediaKinds.Anime) && indexer.Capabilities.Kinds.HasFlag(IndexerMediaKinds.Anime));
            if (!serves)
            {
                continue;
            }

            var availability = governor.GetAvailability(indexer.Id, lane);
            if (!availability.CanRun)
            {
                telemetry.Add(new IndexerRunTelemetry(indexer.Id, "skipped", 0, 0, 0, availability.Reason));
                continue;
            }

            runs.Add(RunIndexerAsync(indexer, distinct, lane, budget, overall.Token, cancellationToken));
        }

        var results = await Task.WhenAll(runs);
        cancellationToken.ThrowIfCancellationRequested();
        telemetry.AddRange(results.Select(result => result.Telemetry));
        var merged = results
            .SelectMany(result => result.Rows)
            .Where(row => !string.IsNullOrWhiteSpace(row.FileUrl))
            .GroupBy(row => CandidateDeduplicator.IdentityKey(row.FileUrl), StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(row => row.Seeders).First())
            .OrderByDescending(row => row.Seeders)
            .ToList();
        return new OrchestratedSearch(merged, telemetry, total.ElapsedMilliseconds);
    }

    private async Task<(IndexerRunTelemetry, List<TorrentSearchResult>)> RunIndexerAsync(
        ITorrentIndexer indexer,
        IReadOnlyList<IndexerQuery> queries,
        SearchLane lane,
        SearchBudget budget,
        CancellationToken overallToken,
        CancellationToken callerToken)
    {
        var watch = Stopwatch.StartNew();
        var rows = new List<TorrentSearchResult>();
        var http = httpFactory(indexer.Id, lane, budget);
        var ran = 0;
        string status = "ok";
        string? detail = null;
        try
        {
            foreach (var query in queries)
            {
                if (query.ImdbId is null && !indexer.Capabilities.SupportsFreeText)
                {
                    continue;
                }

                var found = await indexer.SearchAsync(query, http, overallToken);
                ran++;
                rows.AddRange(found
                    // Knaben flags likely malware; never let those reach the candidate list.
                    .Where(row => row.VirusProbability is null or < 0.5)
                    .Select(row => IndexerResultNormalizer.ToSearchResult(
                        row,
                        indexer.Capabilities.IsMetaIndexer ? IndexerResultNormalizer.EngineNameFor(row, indexer.EngineName) : indexer.EngineName)));
            }
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            status = "timeout";
            detail = $"{budget.Overall.TotalSeconds:0}s search budget";
        }
        catch (IndexerBlockedException ex)
        {
            status = "blocked";
            detail = ex.Message;
        }
        catch (IndexerUnavailableException ex)
        {
            status = "skipped";
            detail = ex.Reason;
        }
        catch (IndexerHttpException ex)
        {
            status = ex.Kind.ToString().ToLowerInvariant();
            detail = ex.Message;
        }
        catch (Exception ex) when (ex is JsonException or XmlException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            // The site answered but we could not parse it (layout/API change): counts towards the breaker.
            governor.Report(indexer.Id, IndexerFailureKind.BadResponse, detail: ex.GetType().Name);
            status = "parse-error";
            detail = ex.Message;
        }

        return (new IndexerRunTelemetry(indexer.Id, status, rows.Count, ran, watch.ElapsedMilliseconds, detail), rows);
    }
}
