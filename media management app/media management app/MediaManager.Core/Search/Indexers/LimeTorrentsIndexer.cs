using System.Text.RegularExpressions;

namespace media_management_app.Search.Indexers;

/// <summary>
/// LimeTorrents HTML search (the best engine of the old plugin set: 10/10 special titles, ~5.5 s).
/// Result rows carry the info-hash in their itorrents .torrent link, so magnets are built without
/// opening detail pages. Rows without such a link (sponsored "Download" ads) are ignored.
/// The domain moves (limetorrents.lol → .fun); redirects are followed.
/// </summary>
public sealed partial class LimeTorrentsIndexer(string baseUrl = "https://www.limetorrents.fun") : ITorrentIndexer
{
    public string Id => "limetorrents";

    public string EngineName => "limetorrents";

    // Paged HTML search: "Title 1080p" reaches 1080p encodes that "Title" leaves on page 5+ (plan 08).
    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All, PrefersSpecificQueries: true);

    private const int MaxPages = 4;
    private const int FullPageRows = 20;

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        // Same depth as the old plugin (4 pages), but only while pages are full and paced by the governor:
        // LimeTorrents is the main source of good movie encodes (plan 08 measurement).
        var results = new List<IndexerResult>();
        // Specific ("title 1080p") queries are already narrow: two pages are enough.
        var maxPages = query.SpecificOnly ? 2 : MaxPages;
        for (var page = 1; page <= maxPages; page++)
        {
            var uri = new Uri($"{baseUrl.TrimEnd('/')}/search/all/{Uri.EscapeDataString(query.Text)}/seeds/{page}/");
            var response = await http.GetAsync(uri, cancellationToken);
            var rows = Parse(response.Body, $"{response.FinalUri.Scheme}://{response.FinalUri.Authority}");
            results.AddRange(rows);
            if (rows.Count < FullPageRows)
            {
                break;
            }
        }

        return results;
    }

    public static IReadOnlyList<IndexerResult> Parse(string html, string siteRoot = "https://www.limetorrents.fun")
    {
        var results = new List<IndexerResult>();
        foreach (Match row in RowRegex().Matches(html))
        {
            results.Add(new IndexerResult(
                Title: IndexerParsing.Decode(row.Groups["name"].Value),
                SizeBytes: IndexerParsing.ParseSize(row.Groups["size"].Value),
                Seeders: IndexerParsing.ParseInt(row.Groups["seed"].Value),
                Leechers: IndexerParsing.ParseInt(row.Groups["leech"].Value),
                InfoHash: row.Groups["hash"].Value,
                MagnetUri: null,
                TorrentUrl: null,
                DetailsUrl: siteRoot.TrimEnd('/') + row.Groups["detail"].Value,
                PublishedUtc: null,
                IndexerId: "limetorrents"));
        }

        if (results.Count == 0 && !html.Contains("tt-name", StringComparison.OrdinalIgnoreCase) && !html.Contains("No results", StringComparison.OrdinalIgnoreCase))
        {
            // Neither results nor a results table: the layout changed or we got an unexpected page.
            throw new FormatException("LimeTorrents page has no result table.");
        }

        return results;
    }

    [GeneratedRegex(
        @"<a href=""https?://itorrents\.[a-z]+/torrent/(?<hash>[0-9A-Fa-f]{40})\.torrent[^""]*""[^>]*></a><a href=""(?<detail>/[^""]+)"">(?<name>[^<]+)</a>.*?<td class=""tdnormal"">(?<size>[^<]+)</td><td class=""tdseed"">(?<seed>[\d,]+)</td><td class=""tdleech"">(?<leech>[\d,]+)</td>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RowRegex();
}
