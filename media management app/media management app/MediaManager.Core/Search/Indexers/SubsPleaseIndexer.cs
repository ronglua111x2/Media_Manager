using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using media_management_app.Services;

namespace media_management_app.Search.Indexers;

/// <summary>
/// SubsPlease release search (https://subsplease.org/api/?f=search&amp;tz=UTC&amp;s=…, JSON, no key). The answer
/// is an object keyed by release ("Kanata kara - 06"), each with one download per resolution; a magnet
/// (base32 btih, size in xl=) is emitted as one row titled "[SubsPlease] {show} - {episode} ({res}p)".
/// "No results" is a bare <c>[]</c>. The API only matches show names (not episode numbers) and reports no
/// swarm counts, so Seeders/Leechers are 0. Captured 12/11/2026: 7 releases × 3 resolutions for "Kanata Kara".
/// </summary>
public sealed class SubsPleaseIndexer(string baseUrl = "https://subsplease.org") : ITorrentIndexer
{
    private static readonly Regex MagnetSize = new(@"[?&]xl=(?<size>\d+)", RegexOptions.Compiled);

    public string Id => "subsplease";

    public string EngineName => "subsplease";

    public IndexerCapabilities Capabilities { get; } = new(IndexerMediaKinds.Anime);

    public async Task<IReadOnlyList<IndexerResult>> SearchAsync(IndexerQuery query, IIndexerHttp http, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{baseUrl.TrimEnd('/')}/api/?f=search&tz=UTC&s={Uri.EscapeDataString(query.Text)}");
        var response = await http.GetAsync(uri, cancellationToken);
        return Parse(response.Body);
    }

    public static IReadOnlyList<IndexerResult> Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException("SubsPlease response is not JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;

            // PHP serializes an empty result set as [] rather than {}.
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0)
            {
                return [];
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("SubsPlease response is neither a release object nor [].");
            }

            var results = new List<IndexerResult>();
            foreach (var release in root.EnumerateObject())
            {
                var entry = release.Value;
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("downloads", out var downloads) ||
                    downloads.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException($"SubsPlease release '{release.Name}' has no downloads array.");
                }

                var show = Str(entry, "show");
                var episode = Str(entry, "episode");
                var baseTitle = string.IsNullOrWhiteSpace(show) ? release.Name.Trim() : show.Trim();
                if (!string.IsNullOrWhiteSpace(show) && !string.IsNullOrWhiteSpace(episode))
                {
                    baseTitle += " - " + episode.Trim();
                }

                var page = Str(entry, "page");
                var detailsUrl = string.IsNullOrWhiteSpace(page) ? null : $"https://subsplease.org/shows/{page.Trim()}/";
                var published = DateTimeOffset.TryParse(
                    Str(entry, "release_date"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal,
                    out var date)
                    ? date
                    : (DateTimeOffset?)null;

                foreach (var download in downloads.EnumerateArray())
                {
                    var magnet = Str(download, "magnet");
                    var hash = CandidateDeduplicator.TryGetInfoHash(magnet);
                    if (hash is null)
                    {
                        continue;
                    }

                    var resolution = Str(download, "res")?.Trim();
                    var label = string.IsNullOrEmpty(resolution)
                        ? string.Empty
                        : resolution.All(char.IsDigit) ? $" ({resolution}p)" : $" ({resolution})";
                    var size = MagnetSize.Match(magnet!);
                    results.Add(new IndexerResult(
                        Title: $"[SubsPlease] {baseTitle}{label}",
                        SizeBytes: size.Success ? IndexerParsing.ParseLong(size.Groups["size"].Value) : 0,
                        Seeders: 0,
                        Leechers: 0,
                        InfoHash: hash,
                        MagnetUri: magnet,
                        TorrentUrl: null,
                        DetailsUrl: detailsUrl,
                        PublishedUtc: published,
                        IndexerId: "subsplease",
                        Source: "subsplease"));
                }
            }

            return results;
        }
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
