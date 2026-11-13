using System.Globalization;
using System.Text.Json;

namespace media_management_app.Search.Indexers;

/// <summary>
/// EZTV official JSON API (<c>GET /api/get-torrents?imdb_id=2861424&amp;limit=100&amp;page=1</c>, no key, no challenge).
/// The HTML search pages sit behind a Cloudflare challenge and are never used. The API only searches by IMDb id,
/// so the indexer declares <c>SupportsFreeText: false</c> and returns nothing for a query without an id.
/// Rows come newest first; with a season/episode filter a second page is fetched only when page 1 had no match
/// and <c>torrents_count</c> says more rows exist (at most <see cref="MaxPages"/> requests per search).
/// </summary>
public sealed class EztvIndexer(string baseUrl = "https://eztvx.to") : ITorrentIndexer
{
    public const int PageSize = 100;

    public const int MaxPages = 2;

    public string Id => "eztv";

    public string EngineName => "eztvx";

    public IndexerCapabilities Capabilities { get; } =
        new(IndexerMediaKinds.TvEpisode | IndexerMediaKinds.TvSeasonPack, SupportsFreeText: false, SupportsImdbLookup: true);

    /// <summary>One parsed API page: the rows kept after filtering and the site's total row count for the id.</summary>
    public sealed record EztvPage(IReadOnlyList<IndexerResult> Rows, int TotalCount);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var imdbDigits = NormalizeImdbId(query.ImdbId);
        if (imdbDigits is null)
        {
            return [];
        }

        var hasFilter = query.Season is not null;
        var results = new List<IndexerResult>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var uri = new Uri($"{baseUrl.TrimEnd('/')}/api/get-torrents?imdb_id={imdbDigits}&limit={PageSize}&page={page}");
            var response = await http.GetAsync(uri, cancellationToken);
            var parsed = ParsePage(response.Body, query.Season, query.Episode);
            results.AddRange(parsed.Rows);

            // Unfiltered: the newest 100 rows are enough. Filtered: stop as soon as something matched
            // or the site has no more rows.
            if (!hasFilter || parsed.Rows.Count > 0 || page * PageSize >= parsed.TotalCount)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>"tt2861424" / "2861424" → "2861424" (leading zeros kept: the API accepts "0944947"). Null when not an id.</summary>
    public static string? NormalizeImdbId(string? imdbId)
    {
        var value = imdbId?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        return value.Length > 0 && value.All(char.IsAsciiDigit) ? value : null;
    }

    public static IReadOnlyList<IndexerResult> Parse(string json, int? season = null, int? episode = null) =>
        ParsePage(json, season, episode).Rows;

    /// <summary>
    /// Parses one API page. With <paramref name="season"/> set, keeps only that season (packs and episodes);
    /// with <paramref name="episode"/> also set, keeps only that exact episode. An episode without a season is ignored.
    /// "No results" is <c>{"torrents_count":0,...}</c> without a "torrents" array.
    /// </summary>
    public static EztvPage ParsePage(string json, int? season = null, int? episode = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("torrents_count", out var countElement))
        {
            throw new FormatException("EZTV response is not a get-torrents object.");
        }

        var totalCount = (int)Number(countElement);
        if (!root.TryGetProperty("torrents", out var torrents) || torrents.ValueKind == JsonValueKind.Null)
        {
            return new EztvPage([], totalCount);
        }

        if (torrents.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("EZTV \"torrents\" is not an array.");
        }

        var results = new List<IndexerResult>();
        var unreadable = 0;
        foreach (var row in torrents.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (season is not null)
            {
                if (Number(row, "season") != season.Value)
                {
                    continue;
                }

                if (episode is not null && Number(row, "episode") != episode.Value)
                {
                    continue;
                }
            }

            var hash = Str(row, "hash");
            var magnet = Str(row, "magnet_url");
            var hasMagnet = magnet?.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) == true;
            var hasHash = IndexerResultNormalizer.IsValidInfoHash(hash);
            if (!hasMagnet && !hasHash)
            {
                unreadable++;
                continue;
            }

            var title = CleanTitle(Str(row, "title")) ?? Str(row, "filename");
            if (string.IsNullOrWhiteSpace(title))
            {
                unreadable++;
                continue;
            }

            var released = Number(row, "date_released_unix");
            results.Add(new IndexerResult(
                Title: title,
                SizeBytes: Number(row, "size_bytes"),
                Seeders: (int)Number(row, "seeds"),
                Leechers: (int)Number(row, "peers"),
                InfoHash: hasHash ? hash : null,
                MagnetUri: hasMagnet ? magnet : null,
                TorrentUrl: null,
                DetailsUrl: null,
                PublishedUtc: IndexerParsing.FromUnix(released),
                IndexerId: "eztv",
                Source: "eztv"));
        }

        IndexerParsing.ThrowIfUnreadable(results.Count, unreadable, "eztv");
        return new EztvPage(results, totalCount);
    }

    // API titles end with a site tag ("...-NTb EZTV"); drop it so the release group parses as "NTb".
    private static string? CleanTitle(string? title)
    {
        var value = title?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.EndsWith(" EZTV", StringComparison.Ordinal) ? value[..^5].TrimEnd() : value;
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? Number(value) : 0;

    // The API mixes types: seeds/peers are numbers, size_bytes/season/episode are strings.
    private static long Number(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt64(out var number) => number,
        JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
        _ => 0
    };
}
