using System.Globalization;
using System.Xml;

namespace media_management_app.Search.Indexers;

/// <summary>
/// Nyaa RSS search (https://nyaa.si/?page=rss&amp;q=…). The feed carries info-hash, seeders, leechers and size
/// in the nyaa: namespace, so no HTML scraping and no detail-page requests are needed. Anime only.
/// </summary>
public sealed class NyaaIndexer(string baseUrl = "https://nyaa.si") : ITorrentIndexer
{
    private const string NyaaNamespace = "https://nyaa.si/xmlns/nyaa";

    public string Id => "nyaa";

    public string EngineName => "nyaasi";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.Anime);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/?page=rss&q={Uri.EscapeDataString(query.Text)}&c=1_0&f=0");
        var response = await http.GetAsync(uri, cancellationToken);
        return Parse(response.Body);
    }

    public static IReadOnlyList<IndexerResult> Parse(string xml)
    {
        var document = new XmlDocument { XmlResolver = null };
        using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
        {
            document.Load(reader);
        }

        var ns = new XmlNamespaceManager(document.NameTable);
        ns.AddNamespace("nyaa", NyaaNamespace);
        var items = document.SelectNodes("/rss/channel/item") ?? throw new FormatException("Nyaa RSS has no channel.");
        var results = new List<IndexerResult>();
        foreach (XmlNode item in items)
        {
            var title = item.SelectSingleNode("title")?.InnerText;
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var pubDate = item.SelectSingleNode("pubDate")?.InnerText;
            results.Add(new IndexerResult(
                Title: title.Trim(),
                SizeBytes: IndexerParsing.ParseSize(item.SelectSingleNode("nyaa:size", ns)?.InnerText),
                Seeders: IndexerParsing.ParseInt(item.SelectSingleNode("nyaa:seeders", ns)?.InnerText),
                Leechers: IndexerParsing.ParseInt(item.SelectSingleNode("nyaa:leechers", ns)?.InnerText),
                InfoHash: item.SelectSingleNode("nyaa:infoHash", ns)?.InnerText?.Trim(),
                MagnetUri: null,
                TorrentUrl: item.SelectSingleNode("link")?.InnerText?.Trim(),
                DetailsUrl: item.SelectSingleNode("guid")?.InnerText?.Trim(),
                PublishedUtc: TryParseRfc822(pubDate),
                IndexerId: "nyaa",
                Source: string.Equals(item.SelectSingleNode("nyaa:trusted", ns)?.InnerText, "Yes", StringComparison.OrdinalIgnoreCase)
                    ? "nyaa:trusted"
                    : "nyaa"));
        }

        return results;
    }

    private static DateTimeOffset? TryParseRfc822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // "Mon, 09 Nov 2026 03:19:53 -0000"
        return DateTimeOffset.TryParseExact(
            value.Trim().Replace("-0000", "+0000"),
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out parsed) ? parsed : null;
    }
}
