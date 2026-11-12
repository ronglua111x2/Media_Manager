using System.Text.Json;

namespace media_management_app.Search.Indexers;

/// <summary>
/// Torrents-CSV JSON API (https://torrents-csv.com/service/search?q=…&amp;size=…, no key). An open
/// database of scraped torrents with info-hash, size, seeders, leechers and creation time.
/// "No results" is <c>{"torrents":[],"next":null}</c>.
/// </summary>
public sealed class TorrentsCsvIndexer(string baseUrl = "https://torrents-csv.com") : ITorrentIndexer
{
    private const int PageSize = 100;

    public string Id => "torrents-csv";

    public string EngineName => "torrentscsv";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/service/search?q={Uri.EscapeDataString(query.Text)}&size={PageSize}");
        var response = await http.GetAsync(uri, cancellationToken);
        return Parse(response.Body);
    }

    public static IReadOnlyList<IndexerResult> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("torrents", out var torrents) ||
            torrents.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Torrents-CSV response has no torrents array.");
        }

        var results = new List<IndexerResult>();
        foreach (var row in torrents.EnumerateArray())
        {
            var hash = Str(row, "infohash");
            var name = Str(row, "name");
            if (!IndexerResultNormalizer.IsValidInfoHash(hash) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var created = Long(row, "created_unix");
            results.Add(new IndexerResult(
                Title: name.Trim(),
                SizeBytes: Long(row, "size_bytes"),
                Seeders: (int)Long(row, "seeders"),
                Leechers: (int)Long(row, "leechers"),
                InfoHash: hash,
                MagnetUri: null,
                TorrentUrl: null,
                DetailsUrl: null,
                PublishedUtc: IndexerParsing.FromUnix(created),
                IndexerId: "torrents-csv",
                Source: "torrents-csv"));
        }

        return results;
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long Long(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String => IndexerParsing.ParseLong(value.GetString()),
            _ => 0
        };
    }
}
