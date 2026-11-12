using System.Globalization;
using System.Text.RegularExpressions;
using media_management_app.Services;

namespace media_management_app.Search.Indexers;

/// <summary>
/// TorrentDownloads search RSS (<c>/rss.xml?type=search&amp;search=…</c>, up to 50 items). The HTML search page
/// (<c>/search/?new=1&amp;s_cat=0&amp;search=…</c>, used by the old qBittorrent plugin) has no info-hash on the list,
/// so the plugin opened one detail page per row; the RSS feed carries <c>info_hash</c>, size, seeders and leechers,
/// so one request per search is enough.
/// Quirks: the feed is served as <c>text/html</c> and Cloudflare appends a <c>&lt;script&gt;</c> after
/// <c>&lt;/rss&gt;</c>, so it is not well-formed XML; items are read with a regex instead of an XML parser.
/// </summary>
public sealed partial class TorrentDownloadsIndexer(string baseUrl = "https://www.torrentdownloads.pro") : ITorrentIndexer
{
    public string Id => "torrentdownloads";

    public string EngineName => "torrentdownloads";

    // The search RSS returns 50 rows: a quality-specific query fills them with relevant encodes (plan 08).
    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All, PrefersSpecificQueries: true);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return [];
        }

        var search = Uri.EscapeDataString(query.Text.Trim()).Replace("%20", "+", StringComparison.Ordinal);
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/rss.xml?type=search&search={search}");
        var response = await http.GetAsync(uri, cancellationToken);
        var rows = Parse(response.Body, $"{response.FinalUri.Scheme}://{response.FinalUri.Authority}");
        return rows.Where(row => MatchesTitleWords(row.Title, query.Text)).ToList();
    }

    /// <summary>
    /// The search RSS matches ANY word: "Golam 1440p" returns 50 unrelated 1440p releases. A row is kept only when
    /// it contains every title word of the query; years and quality markers are optional (the evaluator checks them).
    /// </summary>
    public static bool MatchesTitleWords(string title, string queryText)
    {
        var required = TitleNormalizer.MatchTokens(queryText)
            .Where(token => !NativeQueryText.IsQualityToken(token) && !NativeQueryText.IsYearToken(token))
            .ToList();
        if (required.Count == 0)
        {
            return true;
        }

        var tokens = TitleNormalizer.MatchTokens(title).ToHashSet(StringComparer.Ordinal);
        return required.All(tokens.Contains);
    }

    public static IReadOnlyList<IndexerResult> Parse(string rss, string siteRoot = "https://www.torrentdownloads.pro")
    {
        if (rss.IndexOf("<rss", StringComparison.OrdinalIgnoreCase) < 0 ||
            rss.IndexOf("<channel", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new FormatException("TorrentDownloads response is not an RSS feed.");
        }

        var root = siteRoot.TrimEnd('/');
        var results = new List<IndexerResult>();
        foreach (Match item in ItemRegex().Matches(rss))
        {
            var body = item.Groups["body"].Value;
            var hash = Field(body, "info_hash");
            var title = CleanTitle(Field(body, "title"));
            if (!IndexerResultNormalizer.IsValidInfoHash(hash) || string.IsNullOrEmpty(title))
            {
                continue;
            }

            var link = Field(body, "link");
            results.Add(new IndexerResult(
                Title: title,
                SizeBytes: IndexerParsing.ParseLong(Field(body, "size")),
                Seeders: IndexerParsing.ParseInt(Field(body, "seeders")),
                Leechers: IndexerParsing.ParseInt(Field(body, "leechers")),
                InfoHash: hash,
                MagnetUri: null,
                TorrentUrl: null,
                DetailsUrl: string.IsNullOrEmpty(link) ? null : link.StartsWith('/') ? root + link : link,
                PublishedUtc: TryParseRfc822(Field(body, "pubDate")),
                IndexerId: "torrentdownloads"));
        }

        return results;
    }

    private static string? Field(string itemBody, string name)
    {
        var match = Regex.Match(itemBody, $@"<{name}>(?<value>.*?)</{name}>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value.Trim();
        var cdata = CdataRegex().Match(value);
        return IndexerParsing.Decode(cdata.Success ? cdata.Groups["value"].Value : value);
    }

    // Some uploads are named "-    Coherence 2013 1080p ..."; the leading dash is noise.
    private static string CleanTitle(string? title) =>
        WhitespaceRegex().Replace(title ?? string.Empty, " ").TrimStart('-', ' ').Trim();

    private static DateTimeOffset? TryParseRfc822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // "Mon, 19 Aug 2019 20:00:16 +0200" → "+02:00" for the zzz specifier.
        var normalized = OffsetRegex().Replace(value.Trim(), "$1:$2");
        return DateTimeOffset.TryParseExact(normalized, "ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    [GeneratedRegex(@"<item>(?<body>.*?)</item>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ItemRegex();

    [GeneratedRegex(@"^<!\[CDATA\[(?<value>.*?)\]\]>$", RegexOptions.Singleline)]
    private static partial Regex CdataRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex OffsetRegex();
}
