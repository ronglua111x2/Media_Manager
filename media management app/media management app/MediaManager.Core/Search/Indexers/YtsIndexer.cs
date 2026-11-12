using System.Text.Json;
using System.Text.RegularExpressions;

namespace media_management_app.Search.Indexers;

/// <summary>
/// YTS JSON API (GET /api/v2/list_movies.json, no key). Movies only. Each movie lists one torrent per
/// quality/type, so one row is emitted per torrent with a release-like title built from the movie's
/// title_long ("Coherence (2013)") plus quality and type: "Coherence (2013) [1080p] [bluray] [YTS]".
/// The domain moves (yts.lt / yts.am redirect to yts.gg, yts.mx is dead); redirects are followed.
/// 12/11/2026 the API announced a new base (https://movies-api.accel.li), which serves the same paths.
/// query_term accepts an IMDb id ("tt2866360"), which is used when the query carries one.
/// </summary>
public sealed partial class YtsIndexer(string baseUrl = "https://yts.gg") : ITorrentIndexer
{
    private const int PageSize = 50;

    public string Id => "yts";

    public string EngineName => "yts";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.Movie, SupportsImdbLookup: true);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var term = query.ImdbId is { } imdb && ImdbIdRegex().IsMatch(imdb) ? imdb : query.Text;
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/api/v2/list_movies.json?query_term={Uri.EscapeDataString(term)}&limit={PageSize}");
        var response = await http.GetAsync(uri, cancellationToken);
        return Parse(response.Body);
    }

    public static IReadOnlyList<IndexerResult> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("YTS response has no data object.");
        }

        if (Str(root, "status") is { } status && !string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"YTS answered status '{status}': {Str(root, "status_message")}");
        }

        var results = new List<IndexerResult>();
        if (!data.TryGetProperty("movies", out var movies) || movies.ValueKind != JsonValueKind.Array)
        {
            // movie_count == 0: the "movies" key is simply absent.
            return results;
        }

        foreach (var movie in movies.EnumerateArray())
        {
            var titleLong = Str(movie, "title_long") ?? Str(movie, "title");
            if (string.IsNullOrWhiteSpace(titleLong) ||
                !movie.TryGetProperty("torrents", out var torrents) ||
                torrents.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var torrent in torrents.EnumerateArray())
            {
                var hash = Str(torrent, "hash");
                if (!IndexerResultNormalizer.IsValidInfoHash(hash))
                {
                    continue;
                }

                var title = $"{titleLong.Trim()} [{Str(torrent, "quality")}] [{Str(torrent, "type")}]";
                // Keep x265 variants apart from the default x264 encode of the same quality.
                if (Str(torrent, "video_codec") is { Length: > 0 } codec && !codec.Equals("x264", StringComparison.OrdinalIgnoreCase))
                {
                    title += $" [{codec}]";
                }

                var uploaded = Long(torrent, "date_uploaded_unix");
                results.Add(new IndexerResult(
                    Title: title + " [YTS]",
                    SizeBytes: Long(torrent, "size_bytes"),
                    Seeders: (int)Long(torrent, "seeds"),
                    Leechers: (int)Long(torrent, "peers"),
                    InfoHash: hash,
                    MagnetUri: null,
                    TorrentUrl: Str(torrent, "url"),
                    DetailsUrl: Str(movie, "url"),
                    PublishedUtc: uploaded > 0 ? DateTimeOffset.FromUnixTimeSeconds(uploaded) : null,
                    IndexerId: "yts",
                    Source: "yts"));
            }
        }

        return results;
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Numbers arrive as JSON numbers; tolerate numeric strings too.</summary>
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

    [GeneratedRegex(@"^tt\d{5,10}$", RegexOptions.IgnoreCase)]
    private static partial Regex ImdbIdRegex();
}
