using System.Text.Json;

namespace media_management_app.Search.Indexers;

/// <summary>
/// TsukiHime anime index (https://api.tsukihime.org/v1/search/torrents?q=…, JSON, no key). The API answers
/// plain HTTP clients (the old qBittorrent plugin scraped the site and hit its anti-bot page). Rows carry the
/// release name, hex info-hash ("btih"), total size, source date and the Nyaa id, but no seeder/leecher counts,
/// so Seeders/Leechers are 0. Captured 12/11/2026: 50 of 67 hits per page for "Kanata Kara". Anime only.
/// </summary>
public sealed class TsukiHimeIndexer(string baseUrl = "https://api.tsukihime.org") : ITorrentIndexer
{
    public string Id => "tsukihime";

    public string EngineName => "tsukihime";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.Anime);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/v1/search/torrents?q={Uri.EscapeDataString(query.Text)}");
        var response = await http.GetAsync(uri, cancellationToken);
        return Parse(response.Body);
    }

    public static IReadOnlyList<IndexerResult> Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException("TsukiHime response is not JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("TsukiHime response is not a JSON object.");
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True)
            {
                throw new FormatException("TsukiHime answered with error=true.");
            }

            if (!root.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("TsukiHime response has no results array.");
            }

            var results = new List<IndexerResult>();
            var unreadable = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var title = Str(item, "name");
                var hash = Str(item, "btih");
                if (string.IsNullOrWhiteSpace(title) || !IndexerResultNormalizer.IsValidInfoHash(hash))
                {
                    unreadable++;
                    continue;
                }

                // Adult (Sukebei) releases never belong in anime candidates (audit 07c NS-29).
                if (Long(item, "is_adult") > 0 || Long(item, "sukebei_id") > 0)
                {
                    continue;
                }

                var nyaaId = Long(item, "nyaa_id");
                var sourceDate = Long(item, "source_date");
                results.Add(new IndexerResult(
                    Title: title.Trim(),
                    SizeBytes: Long(item, "totalsize"),
                    Seeders: 0,
                    Leechers: 0,
                    InfoHash: hash!.ToLowerInvariant(),
                    MagnetUri: null,
                    TorrentUrl: nyaaId > 0 ? $"https://nyaa.si/download/{nyaaId}.torrent" : null,
                    DetailsUrl: nyaaId > 0 ? $"https://nyaa.si/view/{nyaaId}" : null,
                    PublishedUtc: IndexerParsing.FromUnix(sourceDate),
                    IndexerId: "tsukihime",
                    Source: "tsukihime",
                    SeedersUnknown: true));
            }

            IndexerParsing.ThrowIfUnreadable(results.Count, unreadable, "tsukihime");
            return results;
        }
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
