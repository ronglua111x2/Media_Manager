using System.Text.Json;
using media_management_app.Services;

namespace media_management_app.Search.Indexers;

/// <summary>
/// NekoBT anime tracker search (https://nekobt.to/api/v1/torrents/search?query=…, JSON, no key; the same
/// endpoint the old qBittorrent "nekobt" plugin used). Rows sit in data.results with title (or auto_title),
/// hex info-hash, magnet, size, seeders and leechers (as strings) and uploaded_at in Unix milliseconds.
/// Captured 12/11/2026: 50 hits (first page, data.more=true) for "Kanata Kara". Anime only.
/// </summary>
public sealed class NekoBtIndexer(string baseUrl = "https://nekobt.to") : ITorrentIndexer
{
    public string Id => "nekobt";

    public string EngineName => "nekobt";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.Anime);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/api/v1/torrents/search?query={Uri.EscapeDataString(query.Text)}");
        var response = await http.GetAsync(uri, cancellationToken);
        return Parse(response.Body, baseUrl);
    }

    public static IReadOnlyList<IndexerResult> Parse(string json, string siteUrl = "https://nekobt.to")
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException("NekoBT response is not JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("NekoBT response is not a JSON object.");
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True)
            {
                throw new FormatException("NekoBT answered with error=true.");
            }

            if (!root.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("results", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("NekoBT response has no data.results array.");
            }

            var site = siteUrl.TrimEnd('/');
            var results = new List<IndexerResult>();
            var unreadable = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    (item.TryGetProperty("deleted", out var deleted) && deleted.ValueKind is not JsonValueKind.Null and not JsonValueKind.False))
                {
                    continue;
                }

                var title = Str(item, "title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = Str(item, "auto_title");
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    unreadable++;
                    continue;
                }

                var magnet = Str(item, "magnet");
                if (string.IsNullOrWhiteSpace(magnet) || !magnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    magnet = null;
                }

                var hash = Str(item, "infohash");
                hash = IndexerResultNormalizer.IsValidInfoHash(hash) ? hash!.ToLowerInvariant() : CandidateDeduplicator.TryGetInfoHash(magnet);
                if (hash is null && magnet is null)
                {
                    unreadable++;
                    continue;
                }

                var id = Str(item, "id");
                var uploadedMs = Long(item, "uploaded_at");
                results.Add(new IndexerResult(
                    Title: title.Trim(),
                    SizeBytes: Long(item, "filesize"),
                    Seeders: (int)Math.Clamp(Long(item, "seeders"), 0, int.MaxValue),
                    Leechers: (int)Math.Clamp(Long(item, "leechers"), 0, int.MaxValue),
                    InfoHash: hash,
                    MagnetUri: magnet,
                    TorrentUrl: null,
                    DetailsUrl: string.IsNullOrWhiteSpace(id) ? null : $"{site}/torrents/{id}",
                    PublishedUtc: IndexerParsing.FromUnixMilliseconds(uploadedMs),
                    IndexerId: "nekobt",
                    Source: "nekobt"));
            }

            IndexerParsing.ThrowIfUnreadable(results.Count, unreadable, "nekobt");
            return results;
        }
    }

    /// <summary>Plain string, or a number rendered as text (ids are 64-bit and may come either way).</summary>
    private static string? Str(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    /// <summary>NekoBT sends sizes, seeders and leechers as strings and dates as numbers; accept both.</summary>
    private static long Long(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.Number when value.TryGetDouble(out var real) => (long)real,
            JsonValueKind.String => IndexerParsing.ParseLong(value.GetString()),
            _ => 0
        };
    }
}
