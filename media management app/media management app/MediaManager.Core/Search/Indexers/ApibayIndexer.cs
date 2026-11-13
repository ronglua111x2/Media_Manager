using System.Text.Json;

namespace media_management_app.Search.Indexers;

/// <summary>
/// The Pirate Bay JSON API (apibay.org, no key). Replaces the "piratebay" and "thepiratebay" qBittorrent
/// plugins, which returned identical rows. "No results" is a single row with an all-zero info-hash.
/// q.php answers at most 100 rows and has no paging (the TPB site itself shows those 100). Two things reach past
/// them (07c NS-30): the search is limited to the Video category (<c>cat=200</c>: probe 13/11/2026, "Coherence"
/// kept exactly the 61 rows of categories 201/202/207/208 and dropped music, audio books and e-books), and the
/// quality forms of a query ("Title 1080p") are sent too, like for the paged HTML sites.
/// </summary>
public sealed class ApibayIndexer(string baseUrl = "https://apibay.org") : ITorrentIndexer
{
    public string Id => "apibay";

    public string EngineName => "thepiratebay";

    public IndexerCapabilities Capabilities { get; } = new(
        IndexerMediaKinds.Movie | IndexerMediaKinds.TvEpisode | IndexerMediaKinds.TvSeasonPack | IndexerMediaKinds.Anime,
        PrefersSpecificQueries: true);

    /// <summary>TPB's "Video" category: 201 Movies, 202 Movies DVDR, 205 TV shows, 207 HD Movies, 208 HD TV shows, 299 Other...</summary>
    public const string VideoCategory = "200";

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/q.php?q={Uri.EscapeDataString(query.Text)}&cat={VideoCategory}");
        var response = await http.GetAsync(uri, cancellationToken);
        return Parse(response.Body, baseUrl);
    }

    public static IReadOnlyList<IndexerResult> Parse(string json, string baseUrl = "https://apibay.org")
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("apibay response is not an array.");
        }

        var results = new List<IndexerResult>();
        var unreadable = 0;
        foreach (var row in document.RootElement.EnumerateArray())
        {
            // The "No results returned" row has id 0 and an all-zero hash.
            if (Str(row, "id") == "0")
            {
                continue;
            }

            var hash = Str(row, "info_hash");
            if (!IndexerResultNormalizer.IsValidInfoHash(hash))
            {
                unreadable++;
                continue;
            }

            var added = IndexerParsing.ParseLong(Str(row, "added"));
            results.Add(new IndexerResult(
                Title: Str(row, "name") ?? string.Empty,
                SizeBytes: IndexerParsing.ParseLong(Str(row, "size")),
                Seeders: IndexerParsing.ParseInt(Str(row, "seeders")),
                Leechers: IndexerParsing.ParseInt(Str(row, "leechers")),
                InfoHash: hash,
                MagnetUri: null,
                TorrentUrl: null,
                DetailsUrl: $"https://thepiratebay.org/description.php?id={Str(row, "id")}",
                PublishedUtc: IndexerParsing.FromUnix(added),
                IndexerId: "apibay",
                Source: Str(row, "status") is { Length: > 0 } status ? $"tpb:{status}" : "tpb"));
        }

        IndexerParsing.ThrowIfUnreadable(results.Count, unreadable, "apibay");
        return results;
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
