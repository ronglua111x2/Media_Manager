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

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/search/all/{Uri.EscapeDataString(query.Text)}/seeds/1/");
        var response = await http.GetAsync(uri, cancellationToken);
        return Parse(response.Body, $"{response.FinalUri.Scheme}://{response.FinalUri.Authority}");
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
