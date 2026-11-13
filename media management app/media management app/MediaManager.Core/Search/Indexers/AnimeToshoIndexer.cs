using System.Globalization;
using System.Text.RegularExpressions;
using media_management_app.Services;

namespace media_management_app.Search.Indexers;

/// <summary>
/// Anime Tosho HTML search (https://animetosho.org/search?q=…). The JSON feed is unusable from here
/// (feed.animetosho.org does not resolve, /feed/json answers 403), so each "home_list_entry" block is read
/// with small, independent regexes: title + details link, exact byte size (from the size tooltip), magnet
/// (base32 btih), "Seeders: n / Leechers: m" tooltip and the UTC submit time. "No items found!" is a genuine
/// empty answer; a page with neither entries nor that marker is a layout change.
/// Note (12/11/2026): the site is flagged "archive" and its newest entries date from May 2026, so it only
/// helps for older series ("Kanata Kara", airing Fall 2026, returns nothing). Anime only.
/// </summary>
public sealed class AnimeToshoIndexer(string baseUrl = "https://animetosho.org") : ITorrentIndexer
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled;

    private static readonly Regex EntryStart = new(@"<div\s+class=""home_list_entry[\s""]", Options);
    private static readonly Regex TitleLink = new(@"<div\s+class=""link""[^>]*>\s*<a\s+href=""(?<url>[^""]+)""[^>]*>(?<title>.*?)</a>", Options);
    private static readonly Regex Magnet = new(@"href=""(?<magnet>magnet:\?[^""]+)""", Options);
    private static readonly Regex TorrentLink = new(@"<a\s+href=""(?<url>[^""]+)""\s+class=""dllink""", Options);
    private static readonly Regex ExactSize = new(@"title=""Total file size:\s*(?<bytes>[\d,]+)\s*bytes""", Options);
    private static readonly Regex SizeText = new(@"<div\s+class=""size""[^>]*>(?<text>[^<]+)</div>", Options);
    private static readonly Regex Swarm = new(@"title=""Seeders:\s*(?<seeders>\d+)\s*/\s*Leechers:\s*(?<leechers>\d+)""", Options);
    private static readonly Regex Submitted = new(@"title=""Date/time submitted:\s*(?<date>[^""]+)""", Options);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);

    public string Id => "animetosho";

    public string EngineName => "animetosho";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.Anime);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query.Text)}");
        var response = await http.GetAsync(uri, cancellationToken);
        return Parse(response.Body);
    }

    public static IReadOnlyList<IndexerResult> Parse(string html)
    {
        var starts = EntryStart.Matches(html).Select(match => match.Index).ToList();
        if (starts.Count == 0)
        {
            if (html.Contains("No items found", StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            throw new FormatException("Anime Tosho page has neither result entries nor the 'No items found' marker.");
        }

        var results = new List<IndexerResult>();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1] : html.Length;
            var block = html.Substring(starts[i], end - starts[i]);

            var link = TitleLink.Match(block);
            var magnetMatch = Magnet.Match(block);
            if (!link.Success || !magnetMatch.Success)
            {
                continue;
            }

            var title = IndexerParsing.Decode(Tags.Replace(link.Groups["title"].Value, string.Empty));
            var magnet = IndexerParsing.Decode(magnetMatch.Groups["magnet"].Value);
            var hash = CandidateDeduplicator.TryGetInfoHash(magnet);
            if (string.IsNullOrWhiteSpace(title) || hash is null)
            {
                continue;
            }

            var exactSize = ExactSize.Match(block);
            var swarm = Swarm.Match(block);
            var torrent = TorrentLink.Match(block);
            results.Add(new IndexerResult(
                Title: title,
                SizeBytes: exactSize.Success
                    ? IndexerParsing.ParseLong(exactSize.Groups["bytes"].Value.Replace(",", string.Empty))
                    : IndexerParsing.ParseSize(SizeText.Match(block).Groups["text"].Value),
                Seeders: swarm.Success ? IndexerParsing.ParseInt(swarm.Groups["seeders"].Value) : 0,
                Leechers: swarm.Success ? IndexerParsing.ParseInt(swarm.Groups["leechers"].Value) : 0,
                InfoHash: hash,
                MagnetUri: magnet,
                TorrentUrl: torrent.Success ? IndexerParsing.Decode(torrent.Groups["url"].Value) : null,
                DetailsUrl: IndexerParsing.Decode(link.Groups["url"].Value),
                PublishedUtc: ParseSubmitted(Submitted.Match(block)),
                IndexerId: "animetosho",
                Source: "animetosho"));
        }

        if (results.Count == 0)
        {
            throw new FormatException($"Anime Tosho page has {starts.Count} entries but none could be parsed.");
        }

        return results;
    }

    /// <summary>"05/05/2026 20:50" (dd/MM/yyyy HH:mm, site time = UTC).</summary>
    private static DateTimeOffset? ParseSubmitted(Match match) =>
        match.Success && DateTimeOffset.TryParseExact(
            match.Groups["date"].Value.Trim(),
            new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
}
