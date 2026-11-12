using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace media_management_app.Search.Indexers;

/// <summary>
/// BT4G RSS search (https://bt4gprx.com/search?q=…&amp;page=rss). A DHT crawler index: anything seen on the
/// DHT shows up, so trust is low and the feed carries no seeders/leechers (always 0). Each item has the
/// magnet in &lt;link&gt;, the detail page in &lt;guid&gt;, an RFC-822-ish pubDate without a space after the comma
/// ("Mon,2 Nov 2026 18:24:57 -0000") and a description "name&lt;br&gt;545.40MB&lt;br&gt;Movie&lt;br&gt;infohash".
/// "No results" is a channel without items.
/// </summary>
public sealed partial class Bt4gIndexer(string baseUrl = "https://bt4gprx.com") : ITorrentIndexer
{
    private static readonly string[] DateFormats =
    [
        "ddd,d MMM yyyy HH:mm:ss zzz",
        "ddd, d MMM yyyy HH:mm:ss zzz",
        "d MMM yyyy HH:mm:ss zzz"
    ];

    public string Id => "bt4g";

    public string EngineName => "bt4g";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.All, IsDhtIndex: true, Trust: IndexerTrust.Low);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query.Text)}&page=rss");
        var response = await http.GetAsync(uri, cancellationToken);
        return Parse(response.Body);
    }

    public static IReadOnlyList<IndexerResult> Parse(string xml)
    {
        var document = new XmlDocument { XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
        }
        catch (XmlException ex)
        {
            // An HTML error page or a truncated body: not the feed we expect.
            throw new FormatException("BT4G response is not valid RSS.", ex);
        }

        if (document.SelectSingleNode("/rss/channel") is null)
        {
            throw new FormatException("BT4G RSS has no channel.");
        }

        var results = new List<IndexerResult>();
        foreach (XmlNode item in document.SelectNodes("/rss/channel/item")!)
        {
            var title = item.SelectSingleNode("title")?.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var link = item.SelectSingleNode("link")?.InnerText?.Trim();
            var magnet = link is not null && link.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) ? link : null;

            // Description: name<br>size<br>category<br>infohash (inside CDATA, so <br> arrives as text).
            var parts = BreakRegex().Split(item.SelectSingleNode("description")?.InnerText ?? string.Empty)
                .Select(part => part.Trim())
                .ToArray();
            var hash = magnet is not null && MagnetHashRegex().Match(magnet) is { Success: true } match
                ? match.Groups["hash"].Value
                : parts.LastOrDefault(IndexerResultNormalizer.IsValidInfoHash);
            if (!IndexerResultNormalizer.IsValidInfoHash(hash))
            {
                continue;
            }

            var category = parts.Length >= 4 ? parts[2] : null;
            results.Add(new IndexerResult(
                Title: title,
                SizeBytes: parts.Length >= 2 ? IndexerParsing.ParseSize(parts[1]) : 0,
                Seeders: 0,
                Leechers: 0,
                InfoHash: hash,
                MagnetUri: magnet,
                TorrentUrl: null,
                DetailsUrl: item.SelectSingleNode("guid")?.InnerText?.Trim(),
                PublishedUtc: ParseDate(item.SelectSingleNode("pubDate")?.InnerText),
                IndexerId: "bt4g",
                Source: string.IsNullOrWhiteSpace(category) ? "bt4g" : $"bt4g:{category}"));
        }

        return results;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().Replace("-0000", "+0000", StringComparison.Ordinal);
        return DateTimeOffset.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out parsed) ? parsed : null;
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"btih:(?<hash>[0-9A-Fa-f]{40})(?![0-9A-Za-z])", RegexOptions.IgnoreCase)]
    private static partial Regex MagnetHashRegex();
}
