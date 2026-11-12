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
