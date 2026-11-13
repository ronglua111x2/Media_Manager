using System.Text.RegularExpressions;

namespace media_management_app.Search.Indexers;

/// <summary>
/// Kickass Torrents HTML search (<c>https://katcr.to/search/&lt;query&gt;/&lt;page&gt;/</c>, 20 rows per page).
/// The search list has NO info-hash and NO magnet: each row only links to its detail page
/// (<c>/&lt;slug&gt;-t&lt;id&gt;.html</c>) and to <c>/download/&lt;slug&gt;-t&lt;id&gt;.html</c>, which is another HTML page.
/// The old qBittorrent plugin opened one detail page per row to read the magnet, which is why it was slow
/// (median 60 s, sometimes hung). This indexer returns rows with <see cref="IndexerResult.DetailsUrl"/> set and
/// <see cref="IndexerResult.InfoHash"/> / <see cref="IndexerResult.MagnetUri"/> null; the orchestrator then opens the
/// detail pages of at most a few rows the recipe could accept (<see cref="IDetailPageResolver"/>, governed and counted
/// like any request) and drops the rest, which could not be added anyway.
/// "No results" is not a message: the site answers an unknown query with a decoy torrent detail page
/// (<c>id="mainDetailsTable"</c>, a made-up torrent named after the query, unrelated magnet). That page is treated
/// as an empty result and its magnet is never read.
/// </summary>
public sealed partial class KickassIndexer(string baseUrl = "https://katcr.to") : ITorrentIndexer, IDetailPageResolver
{
    public async Task<string?> ResolveMagnetAsync(string detailsUrl, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(new Uri(detailsUrl), cancellationToken);
        return ParseDetailMagnet(response.Body);
    }

    /// <summary>The first magnet link with a valid info-hash on a detail page.</summary>
    public static string? ParseDetailMagnet(string html)
    {
        var match = MagnetRegex().Match(html);
        return match.Success && IndexerResultNormalizer.IsValidInfoHash(match.Groups["hash"].Value)
            ? IndexerParsing.Decode(match.Groups["magnet"].Value)
            : null;
    }

    private const string ResultTableMarker = "class=\"data frontPageWidget\"";

    public string Id => "kickass";

    public string EngineName => "kickasstorrents";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All);

    private const int MaxPages = 2;
    private const int FullPageRows = 20;

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return [];
        }

        var results = new List<IndexerResult>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var uri = new Uri($"{baseUrl.TrimEnd('/')}/search/{Uri.EscapeDataString(query.Text.Trim())}/{page}/");
            var response = await IndexerParsing.NextPageAsync(() => http.GetAsync(uri, cancellationToken), page == 1);
            if (response is null)
            {
                break;
            }

            var rows = Parse(response.Body, $"{response.FinalUri.Scheme}://{response.FinalUri.Authority}");
            results.AddRange(rows);
            if (rows.Count < FullPageRows)
            {
                break;
            }
        }

        return results;
    }

    public static IReadOnlyList<IndexerResult> Parse(string html, string siteRoot = "https://katcr.to")
    {
        var tableStart = html.IndexOf(ResultTableMarker, StringComparison.OrdinalIgnoreCase);
        if (tableStart < 0)
        {
            if (html.Contains("id=\"mainDetailsTable\"", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("Nothing found", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("did not match any documents", StringComparison.OrdinalIgnoreCase))
            {
                // Decoy detail page / "nothing found": a genuine empty result.
                return [];
            }

            throw new FormatException("Kickass page has no result table.");
        }

        var tableEnd = html.IndexOf("</table>", tableStart, StringComparison.OrdinalIgnoreCase);
        var table = tableEnd < 0 ? html[tableStart..] : html[tableStart..tableEnd];
        var rowChunks = RowStartRegex().Split(table).Skip(1).ToList();

        var root = siteRoot.TrimEnd('/');
        var results = new List<IndexerResult>();
        foreach (var chunk in rowChunks)
        {
            var row = RowRegex().Match(chunk);
            if (!row.Success)
            {
                continue;
            }

            var title = WhitespaceRegex().Replace(IndexerParsing.Decode(TagRegex().Replace(row.Groups["name"].Value, string.Empty)), " ").Trim();
            if (title.Length == 0)
            {
                continue;
            }

            var detail = row.Groups["detail"].Value;
            results.Add(new IndexerResult(
                Title: title,
                SizeBytes: IndexerParsing.ParseSize(row.Groups["size"].Value),
                Seeders: IndexerParsing.ParseInt(row.Groups["seed"].Value),
                Leechers: IndexerParsing.ParseInt(row.Groups["leech"].Value),
                InfoHash: null,
                MagnetUri: null,
                TorrentUrl: null,
                DetailsUrl: detail.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? detail : root + detail,
                PublishedUtc: null,
                IndexerId: "kickass"));
        }

        if (results.Count == 0 && rowChunks.Count > 0)
        {
            throw new FormatException("Kickass result rows found but none could be parsed.");
        }

        return results;
    }

    [GeneratedRegex(@"href=""(?<magnet>magnet:\?xt=urn:btih:(?<hash>[0-9a-fA-F]{40})[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex MagnetRegex();

    [GeneratedRegex(@"<tr\s+class=""(?:odd|even)""\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex RowStartRegex();

    [GeneratedRegex(
        @"<a\s+href=""(?<detail>[^""]+)""\s+class=""cellMainLink"">(?<name>.*?)</a>.*?<td\s+class=""nobr center"">\s*(?<size>[^<]+?)\s*</td>.*?<td\s+class=""green center"">\s*(?<seed>[\d,]+)\s*</td>\s*<td\s+class=""red lasttd center"">\s*(?<leech>[\d,]+)\s*</td>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RowRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
