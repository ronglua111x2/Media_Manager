using System.Text.Json;
using System.Text.Json.Nodes;

namespace media_management_app.Search.Indexers;

/// <summary>
/// Knaben meta-search API (https://api.knaben.org/v1, JSON, no key). Aggregates The Pirate Bay, Nyaa,
/// TokyoTosho, YTS and 1337x (whose own site is behind a challenge), with info-hash, magnet, seeders,
/// size, date and a virus-detection score. Measured 12/11/2026: 150 hits in 1.7 s for "Coherence".
/// </summary>
public sealed class KnabenIndexer(string baseUrl = "https://api.knaben.org") : ITorrentIndexer
{
    private const int PageSize = 100;
    private const int MaxPages = 2;

    public string Id => "knaben";

    public string EngineName => "knaben";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All, IsMetaIndexer: true);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        // A full page means there is more: fetch one more (governed) page, never an unbounded crawl.
        var results = new List<IndexerResult>();
        for (var page = 0; page < MaxPages; page++)
        {
            var body = new JsonObject
            {
                ["search_type"] = "100%",
                ["search_field"] = "title",
                ["query"] = query.Text,
                ["order_by"] = "seeders",
                ["order_direction"] = "desc",
                ["from"] = page * PageSize,
                ["size"] = PageSize,
                ["hide_unsafe"] = true,
                ["hide_xxx"] = true
            };
            var response = await http.PostJsonAsync(new Uri(baseUrl.TrimEnd('/') + "/v1"), body.ToJsonString(), cancellationToken);
            var rows = Parse(response.Body);
            results.AddRange(rows);
            if (rows.Count < PageSize)
            {
                break;
            }
        }

        return results;
    }

    public static IReadOnlyList<IndexerResult> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Knaben response has no hits array.");
        }

        var results = new List<IndexerResult>();
        foreach (var hit in hits.EnumerateArray())
        {
            var title = Str(hit, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            results.Add(new IndexerResult(
                Title: title,
                SizeBytes: hit.TryGetProperty("bytes", out var bytes) && bytes.TryGetInt64(out var size) ? size : 0,
                Seeders: Int(hit, "seeders"),
                Leechers: Int(hit, "peers"),
                InfoHash: Str(hit, "hash"),
                MagnetUri: Str(hit, "magnetUrl"),
                TorrentUrl: Str(hit, "link"),
                DetailsUrl: Str(hit, "details"),
                PublishedUtc: DateTimeOffset.TryParse(Str(hit, "date"), out var date) ? date : null,
                IndexerId: "knaben",
                Source: Str(hit, "tracker"),
                VirusProbability: hit.TryGetProperty("virusDetection", out var virus) && virus.TryGetDouble(out var probability) ? probability : null));
        }

        return results;
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
