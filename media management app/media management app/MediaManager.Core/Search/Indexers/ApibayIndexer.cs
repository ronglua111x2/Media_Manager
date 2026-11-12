using System.Text.Json;

namespace media_management_app.Search.Indexers;

/// <summary>
/// The Pirate Bay JSON API (apibay.org, no key). Replaces the "piratebay" and "thepiratebay" qBittorrent
/// plugins, which returned identical rows. "No results" is a single row with an all-zero info-hash.
/// </summary>
public sealed class ApibayIndexer(string baseUrl = "https://apibay.org") : ITorrentIndexer
{
    public string Id => "apibay";

    public string EngineName => "thepiratebay";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.Movie | IndexerMediaKinds.TvEpisode | IndexerMediaKinds.TvSeasonPack | IndexerMediaKinds.Anime);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/q.php?q={Uri.EscapeDataString(query.Text)}&cat=");
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
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var hash = Str(row, "info_hash");
            if (!IndexerResultNormalizer.IsValidInfoHash(hash) || Str(row, "id") == "0")
            {
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
                PublishedUtc: added > 0 ? DateTimeOffset.FromUnixTimeSeconds(added) : null,
                IndexerId: "apibay",
                Source: Str(row, "status") is { Length: > 0 } status ? $"tpb:{status}" : "tpb"));
        }

        return results;
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
