using System.Globalization;
using System.Text.RegularExpressions;

namespace media_management_app.Search.Indexers;

/// <summary>
/// Bitsearch HTML search (https://bitsearch.eu/search?q=…; bitsearch.to and solidtorrents.to redirect there).
/// A DHT-crawled index with tracker-scraped seeders. Each result is a
/// <c>&lt;div class="torrent-result-card …"&gt;</c> holding the name link (/torrent/&lt;id&gt;), size, date
/// (M/d/yyyy), seeders, leechers and a magnet (repeated for the mobile layout). The page is cut into
/// cards first and each field is matched inside its card, so ads between cards are never mistaken for
/// rows and a card without a magnet / info-hash is skipped.
/// "No results" is an empty results container plus a "No results found" message.
/// </summary>
public sealed partial class BitsearchIndexer(string baseUrl = "https://bitsearch.eu") : ITorrentIndexer
{
    private const string CardMarker = "<div class=\"torrent-result-card";

    public string Id => "bitsearch";

    public string EngineName => "bitsearch";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All, IsDhtIndex: true, PrefersSpecificQueries: true);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query.Text)}");
        var response = await http.GetAsync(uri, cancellationToken);
        return Parse(response.Body, $"{response.FinalUri.Scheme}://{response.FinalUri.Authority}");
    }

    public static IReadOnlyList<IndexerResult> Parse(string html, string siteRoot = "https://bitsearch.eu")
    {
        var cards = html.Split(CardMarker, StringSplitOptions.None).Skip(1).ToList();
        var results = new List<IndexerResult>();
        foreach (var card in cards)
        {
            var name = NameRegex().Match(card);
            if (!name.Success)
            {
                continue;
            }

            var magnetMatch = MagnetRegex().Match(card);
            var magnet = magnetMatch.Success ? IndexerParsing.Decode(magnetMatch.Groups["magnet"].Value) : null;
            var hashMatch = HashRegex().Match(card);
            var hash = hashMatch.Success ? hashMatch.Groups["hash"].Value : null;
            if (!IndexerResultNormalizer.IsValidInfoHash(hash))
            {
                continue;
            }

            var title = IndexerParsing.Decode(name.Groups["name"].Value);
            if (title.Length == 0)
            {
                continue;
            }

            var date = DateRegex().Match(card);
            results.Add(new IndexerResult(
                Title: title,
                SizeBytes: IndexerParsing.ParseSize(SizeRegex().Match(card).Groups["size"].Value),
                Seeders: IndexerParsing.ParseInt(SeedersRegex().Match(card).Groups["count"].Value),
                Leechers: IndexerParsing.ParseInt(LeechersRegex().Match(card).Groups["count"].Value),
                InfoHash: hash,
                MagnetUri: magnet,
                TorrentUrl: null,
                DetailsUrl: siteRoot.TrimEnd('/') + name.Groups["detail"].Value,
                PublishedUtc: date.Success &&
                              DateTime.TryParseExact(date.Groups["date"].Value.Trim(), "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var published)
                    ? new DateTimeOffset(published, TimeSpan.Zero)
                    : null,
                IndexerId: "bitsearch",
                Source: VerifiedRegex().IsMatch(card) ? "bitsearch:verified" : "bitsearch"));
        }

        if (results.Count == 0)
        {
            if (cards.Count > 0)
            {
                throw new FormatException("Bitsearch result cards found but none could be parsed.");
            }

            if (!html.Contains("No results found", StringComparison.OrdinalIgnoreCase))
            {
                // Neither result cards nor the "no results" message: layout change or an unexpected page.
                throw new FormatException("Bitsearch page has no result list.");
            }
        }

        return results;
    }

    [GeneratedRegex(@"<h3\b[^>]*>\s*<a\s+href=""(?<detail>/torrent/[^""]+)""[^>]*>(?<name>[^<]+)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"href=""(?<magnet>magnet:\?[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex MagnetRegex();

    /// <summary>Hash from the magnet ("btih:") or, failing that, the .torrent download link.</summary>
    [GeneratedRegex(@"(?:btih:|/download/torrent/)(?<hash>[0-9A-Fa-f]{40})(?![0-9A-Za-z])", RegexOptions.IgnoreCase)]
    private static partial Regex HashRegex();

    /// <summary>The first bare &lt;span&gt; holding a size ("1.24 GB"); the download count span has a class.</summary>
    [GeneratedRegex(@"<span>\s*(?<size>[\d.,]+\s*(?:[KMGT]i?B|B|bytes))\s*</span>", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"fa-calendar[^>]*></i>\s*<span>(?<date>[^<]+)</span>", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@">\s*(?<count>[\d,]+)\s*</span>\s*<span>\s*seeders\s*</span>", RegexOptions.IgnoreCase)]
    private static partial Regex SeedersRegex();

    [GeneratedRegex(@">\s*(?<count>[\d,]+)\s*</span>\s*<span>\s*leechers\s*</span>", RegexOptions.IgnoreCase)]
    private static partial Regex LeechersRegex();

    [GeneratedRegex(@"fa-check-circle[^>]*></i>\s*Verified", RegexOptions.IgnoreCase)]
    private static partial Regex VerifiedRegex();
}
