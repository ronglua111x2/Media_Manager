using System.Text.Json;

namespace media_management_app.Search.Indexers;

/// <summary>
/// Torrents-CSV JSON API (https://torrents-csv.com/service/search?q=…&amp;size=…, no key). An open
/// database of scraped torrents with info-hash, size, seeders, leechers and creation time.
/// "No results" is <c>{"torrents":[],"next":null}</c>. The server answers at most 10 rows per request whatever
/// <c>size</c> asks, with a <c>next</c> cursor that <c>&amp;after=&lt;next&gt;</c> continues (probe 13/11/2026: the
/// follow-up page is not a repeat of the first); up to <see cref="MaxPages"/> pages are read (07c NS-30).
/// </summary>
public sealed class TorrentsCsvIndexer(string baseUrl = "https://torrents-csv.com") : ITorrentIndexer
{
    private const int PageSize = 100;
    private const int ServerPageRows = 10;
    private const int MaxPages = 3;

    public string Id => "torrents-csv";

    public string EngineName => "torrentscsv";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var results = new List<IndexerResult>();
        string? after = null;
        for (var page = 1; page <= MaxPages; page++)
        {
            var uri = new Uri($"{baseUrl.TrimEnd('/')}/service/search?q={Uri.EscapeDataString(query.Text)}&size={PageSize}{(after is null ? "" : $"&after={Uri.EscapeDataString(after)}")}");
            var response = await IndexerParsing.NextPageAsync(() => http.GetAsync(uri, cancellationToken), page == 1);
            if (response is null)
            {
                break;
            }

            var rows = Parse(response.Body);
            results.AddRange(rows);
            after = NextCursor(response.Body);
            if (after is null || rows.Count < ServerPageRows)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>The <c>next</c> cursor of a response (a number or a string), or null at the end.</summary>
    public static string? NextCursor(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("next", out var next))
        {
            return null;
        }

        return next.ValueKind switch
        {
            JsonValueKind.Number => next.GetRawText(),
            JsonValueKind.String when !string.IsNullOrWhiteSpace(next.GetString()) => next.GetString(),
            _ => null
        };
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
        var unreadable = 0;
        foreach (var row in torrents.EnumerateArray())
        {
            var hash = Str(row, "infohash");
            var name = Str(row, "name");
            if (!IndexerResultNormalizer.IsValidInfoHash(hash) || string.IsNullOrWhiteSpace(name))
            {
                unreadable++;
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

        IndexerParsing.ThrowIfUnreadable(results.Count, unreadable, "torrents-csv");
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
