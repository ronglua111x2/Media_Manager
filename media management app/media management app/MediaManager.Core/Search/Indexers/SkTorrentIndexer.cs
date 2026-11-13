using System.Globalization;
using System.Text.RegularExpressions;

namespace media_management_app.Search.Indexers;

/// <summary>
/// SkTorrent (Czech/Slovak tracker) HTML search:
/// <c>https://sktorrent.eu/torrent/torrents_v2.php?search=…&amp;category=0&amp;zaner=&amp;jazyk=&amp;active=0&amp;page=N</c>
/// (0-based pages, 21 cards per full page, newest first). The site is xbtit-based: each card's detail link
/// (<c>details.php?name=…&amp;id=&lt;40 hex&gt;</c>) carries the info-hash as its id, so no detail page is opened.
/// A card shows "Velkost &lt;size&gt; | Pridany dd/MM/yyyy", "Odosielaju : &lt;seeders&gt;" and "Stahuju : &lt;leechers&gt;".
/// Titles end with a ČSFD rating ("… = CSFD 73%"), which is dropped.
/// The magnet adds the site's passkey-free UDP tracker to the usual public ones (the old plugin's HTTP announce
/// URL carried a personal "pid" passkey and is not used).
/// "No results" is the message "Nenasli ste co ste hladali".
/// </summary>
public sealed partial class SkTorrentIndexer(string baseUrl = "https://sktorrent.eu") : ITorrentIndexer
{
    private const string SiteTracker = "udp://ipv4announce.sktorrent.eu:6969/announce";

    public string Id => "sktorrent";

    public string EngineName => "sktorrent";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.Movie | IndexerMediaKinds.TvEpisode | IndexerMediaKinds.TvSeasonPack);

    private const int MaxPages = 2;
    private const int FullPageRows = 21;

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return [];
        }

        var search = Uri.EscapeDataString(query.Text.Trim());
        var results = new List<IndexerResult>();
        for (var page = 0; page < MaxPages; page++)
        {
            var uri = new Uri($"{baseUrl.TrimEnd('/')}/torrent/torrents_v2.php?search={search}&category=0&zaner=&jazyk=&active=0&page={page}");
            var response = await IndexerParsing.NextPageAsync(() => http.GetAsync(uri, cancellationToken), page == 0);
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

    public static IReadOnlyList<IndexerResult> Parse(string html, string siteRoot = "https://sktorrent.eu")
    {
        var root = siteRoot.TrimEnd('/');
        var results = new List<IndexerResult>();
        foreach (Match card in CardRegex().Matches(html))
        {
            var hash = card.Groups["hash"].Value;
            var title = CsfdSuffixRegex().Replace(IndexerParsing.Decode(card.Groups["name"].Value), string.Empty).Trim();
            if (!IndexerResultNormalizer.IsValidInfoHash(hash) || title.Length == 0)
            {
                continue;
            }

            results.Add(new IndexerResult(
                Title: title,
                SizeBytes: IndexerParsing.ParseSize(card.Groups["size"].Value),
                Seeders: IndexerParsing.ParseInt(card.Groups["seed"].Value),
                Leechers: IndexerParsing.ParseInt(card.Groups["leech"].Value),
                InfoHash: hash,
                MagnetUri: IndexerResultNormalizer.BuildMagnet(hash, title) + "&tr=" + Uri.EscapeDataString(SiteTracker),
                TorrentUrl: null,
                // The site's own link carries "name=<slug>-<hex of the requester's IP>"; keep only the id.
                DetailsUrl: $"{root}/torrent/details.php?id={hash}",
                PublishedUtc: DateTime.TryParseExact(card.Groups["date"].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var published)
                    ? new DateTimeOffset(published, TimeSpan.Zero)
                    : null,
                IndexerId: "sktorrent"));
        }

        if (results.Count == 0)
        {
            if (html.Contains("details.php?", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("SkTorrent result cards found but none could be parsed.");
            }

            if (!html.Contains("Nenasli ste co ste hladali", StringComparison.OrdinalIgnoreCase))
            {
                // Neither result cards nor the "nothing found" message: layout change or an unexpected page.
                throw new FormatException("SkTorrent page has no result list.");
            }
        }

        return results;
    }

    [GeneratedRegex(
        @"<a\s+href=""(?<detail>details\.php\?[^""]*?\bid=(?<hash>[0-9A-Fa-f]{40}))""[^>]*>(?:(?!</a>).)*?<br>(?<name>[^<]*)</a>\s*<br>\s*Velkost\s+(?<size>[^|<]+?)\s*\|\s*Pridany\s+(?<date>\d{2}/\d{2}/\d{4})\s*<br>\s*Odosielaju\s*:\s*(?<seed>\d+)\s*<br>\s*Stahuju\s*:\s*(?<leech>\d+)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CardRegex();

    [GeneratedRegex(@"\s*=\s*CSFD\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex CsfdSuffixRegex();
}
