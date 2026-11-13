using FluentAssertions;
using media_management_app.Search;
using media_management_app.Search.Indexers;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Search;

/// <summary>Kickass (katcr.to) and SkTorrent HTML parsers against real pages captured 12/11/2026.</summary>
public class KickassSkTorrentIndexerTests
{
    private static string Fixture(string indexer, string file) =>
        File.ReadAllText(GoldenFixtureFile.PathTo(Path.Combine("Indexers", indexer, file)));

    [Fact]
    public void Kickass_ParsesListRowsWithoutHash()
    {
        var rows = KickassIndexer.Parse(Fixture("kickass", "coherence.html"));

        rows.Should().HaveCount(20);
        // The search list carries no info-hash or magnet; only the detail page has one (never opened).
        rows.Should().OnlyContain(row => row.InfoHash == null && row.MagnetUri == null && row.DetailsUrl != null);

        var first = rows[0];
        first.Title.Should().Be("Coherence X 6 1 macOS pac");
        first.SizeBytes.Should().BeCloseTo((long)(24.9 * 1024 * 1024), 1024);
        first.Seeders.Should().Be(4);
        first.Leechers.Should().Be(0);
        first.DetailsUrl.Should().Be("https://katcr.to/coherence-x-6-1-macos-pac-t6726256.html");
        first.IndexerId.Should().Be("kickass");

        var movie = rows.Single(row => row.Title.StartsWith("Coherence (2013) (1080p BluRay x265", StringComparison.Ordinal));
        movie.Title.Should().Be("Coherence (2013) (1080p BluRay x265 HEVC 10bit AAC 5 1 English + German MONOLITH) QxR");
        movie.SizeBytes.Should().BeCloseTo((long)(3.8 * 1024 * 1024 * 1024), 1024 * 1024);
        movie.Seeders.Should().Be(86);
        movie.Leechers.Should().Be(15);
    }

    private sealed class KickassSite(string search, string empty) : HttpMessageHandler
    {
        public List<string> DetailPages { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path.StartsWith("/search/", StringComparison.Ordinal))
            {
                body = path.EndsWith("/1/", StringComparison.Ordinal) ? search : empty;
            }
            else
            {
                lock (DetailPages)
                {
                    DetailPages.Add(path);
                }

                var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
                body = $"<html><body><a class=\"kaGiantButton\" href=\"magnet:?xt=urn:btih:{hash}&amp;dn=x&amp;tr=udp%3A%2F%2Ftracker\">Magnet</a></body></html>";
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html"),
                RequestMessage = request
            });
        }
    }

    [Fact]
    public async Task Kickass_OpensDetailPagesOnlyForRowsThatCouldMatter()
    {
        var site = new KickassSite(Fixture("kickass", "coherence.html"), Fixture("kickass", "empty.html"));
        var governor = new IndexerRequestGovernor(TimeProvider.System, _ => new IndexerPolicy(1000, 2, TimeSpan.Zero, 1000));
        var cache = new IndexerResponseCache();
        var client = new HttpClient(site);
        var orchestrator = new SearchOrchestrator(governor, (id, lane, budget) =>
            new GovernedIndexerHttp(client, governor, cache, id, lane, budget.MaxGovernorWait, TimeSpan.FromMinutes(45), "test", budget.PerIndexer));

        var outcome = await orchestrator.SearchAsync(
            [new IndexerQuery("Coherence", media_management_app.Common.MediaKind.Movie)],
            [new KickassIndexer()],
            SearchLane.Interactive,
            SearchBudget.Interactive,
            CancellationToken.None,
            row => row.FileUrl.StartsWith("magnet:", StringComparison.Ordinal) && row.FileName.Contains("1080p", StringComparison.Ordinal) && row.FileName.Contains("2013", StringComparison.Ordinal));

        // Only the best-seeded rows the recipe could accept, at most three detail pages; the rest is dropped.
        site.DetailPages.Should().HaveCount(SearchOrchestrator.MaxDetailLookups);
        outcome.Results.Should().HaveCount(SearchOrchestrator.MaxDetailLookups);
        outcome.Results.Should().OnlyContain(row => row.FileUrl.StartsWith("magnet:?xt=urn:btih:", StringComparison.Ordinal) && row.FileName.Contains("1080p"));
        outcome.Indexers.Single().Requests.Should().Be(2 + SearchOrchestrator.MaxDetailLookups);
    }

    [Fact]
    public void Kickass_DetailPageMagnet_IsRead()
    {
        KickassIndexer.ParseDetailMagnet("<a href=\"magnet:?xt=urn:btih:0123456789ABCDEF0123456789abcdef01234567&amp;dn=Coherence\">x</a>")
            .Should().Be("magnet:?xt=urn:btih:0123456789ABCDEF0123456789abcdef01234567&dn=Coherence");
        KickassIndexer.ParseDetailMagnet("<html>no magnet</html>").Should().BeNull();
    }

    [Fact]
    public void Kickass_DecoyNoResultPage_ReturnsNoRows()
    {
        // Unknown queries get a fake detail page with an unrelated magnet; it must not become a row.
        KickassIndexer.Parse(Fixture("kickass", "empty.html")).Should().BeEmpty();
    }

    [Fact]
    public void Kickass_UnexpectedPage_Throws()
    {
        var parse = () => KickassIndexer.Parse("<html><title>Kickass Torrents</title><body>Maintenance</body></html>");
        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void Kickass_IdentityMatchesOldPlugin()
    {
        var indexer = new KickassIndexer();
        indexer.Id.Should().Be("kickass");
        indexer.EngineName.Should().Be("kickasstorrents");
    }

    [Fact]
    public void SkTorrent_ParsesCardsWithHashFromDetailLink()
    {
        var rows = SkTorrentIndexer.Parse(Fixture("sktorrent", "coherence.html"));

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(row => IndexerResultNormalizer.IsValidInfoHash(row.InfoHash));

        var first = rows[0];
        first.Title.Should().Be("Coherence (2013)[BluRay][1080p]");
        first.InfoHash.Should().Be("a5aa8e17ee4943698a476068d3882f3236eb9191");
        CandidateDeduplicator.TryGetInfoHash(first.MagnetUri).Should().Be("a5aa8e17ee4943698a476068d3882f3236eb9191");
        first.MagnetUri.Should().Contain("&tr=" + Uri.EscapeDataString("udp://ipv4announce.sktorrent.eu:6969/announce"));
        first.SizeBytes.Should().BeCloseTo((long)(1.2 * 1024 * 1024 * 1024), 1024 * 1024);
        first.Seeders.Should().Be(10);
        first.Leechers.Should().Be(0);
        first.PublishedUtc.Should().Be(new DateTimeOffset(2020, 8, 11, 0, 0, 0, TimeSpan.Zero));
        first.DetailsUrl.Should().Be("https://sktorrent.eu/torrent/details.php?id=a5aa8e17ee4943698a476068d3882f3236eb9191");
        first.IndexerId.Should().Be("sktorrent");

        var webrip = rows[2];
        webrip.Title.Should().Be("Coherence (2013)[WebRip]");
        webrip.InfoHash.Should().Be("6ae1149dd5d09d5946c9d4cb9f6baaa3e7850d6e");
        webrip.SizeBytes.Should().BeCloseTo((long)(829.7 * 1024 * 1024), 1024);
        webrip.Seeders.Should().Be(0);
        webrip.Leechers.Should().Be(1);
        webrip.PublishedUtc.Should().Be(new DateTimeOffset(2014, 8, 20, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void SkTorrent_NoResultPage_ReturnsNoRows()
    {
        SkTorrentIndexer.Parse(Fixture("sktorrent", "empty.html")).Should().BeEmpty();
    }

    [Fact]
    public void SkTorrent_UnexpectedPage_Throws()
    {
        var parse = () => SkTorrentIndexer.Parse("<html><title>Sk-CzTorrent</title><body>Udrzba</body></html>");
        parse.Should().Throw<FormatException>();
    }
}
