using FluentAssertions;
using media_management_app.Search;
using media_management_app.Search.Indexers;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Search;

/// <summary>YTS, Torrents-CSV, BT4G and Bitsearch parsers against real responses captured 12/11/2026.</summary>
public class MovieGeneralIndexerTests
{
    private static string Fixture(string indexer, string file) =>
        File.ReadAllText(GoldenFixtureFile.PathTo(Path.Combine("Indexers", indexer, file)));

    [Fact]
    public void Yts_EmitsOneRowPerTorrentWithReleaseLikeTitle()
    {
        var rows = YtsIndexer.Parse(Fixture("yts", "coherence.json"));

        rows.Should().HaveCount(2);
        var first = rows[0];
        first.Title.Should().Be("Coherence (2013) [720p] [bluray] [YTS]");
        first.InfoHash.Should().Be("A78323E41EA96EBC9DC7292C62B11D302B3C7C29");
        first.SizeBytes.Should().Be(736_970_670);
        first.Seeders.Should().Be(36);
        first.Leechers.Should().Be(11);
        first.PublishedUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_446_327_196));
        first.DetailsUrl.Should().Be("https://yts.gg/movies/coherence-2013");
        first.IndexerId.Should().Be("yts");

        rows[1].Title.Should().Be("Coherence (2013) [1080p] [bluray] [YTS]");
        rows[1].InfoHash.Should().Be("95863294E0C5A182FC358C4C09C9DEA26330DF71");
        rows[1].Seeders.Should().Be(100);
        rows[1].SizeBytes.Should().Be(1_331_439_862);

        IndexerResultNormalizer.ToSearchResult(first, "yts").FileUrl
            .Should().StartWith("magnet:?xt=urn:btih:a78323e41ea96ebc9dc7292c62b11d302b3c7c29");
    }

    [Fact]
    public void Yts_Empty_ReturnsNoRows()
    {
        YtsIndexer.Parse(Fixture("yts", "empty.json")).Should().BeEmpty();
    }

    [Fact]
    public void Yts_UnexpectedShape_Throws()
    {
        var parse = () => YtsIndexer.Parse("""{"status":"error","status_message":"bad"}""");
        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void TorrentsCsv_ParsesRows()
    {
        var rows = TorrentsCsvIndexer.Parse(Fixture("torrents-csv", "coherence.json"));

        rows.Should().HaveCount(10);
        var first = rows[0];
        first.Title.Should().Be("Coherence (2013) 1080p BrRip x264 - YIFY");
        first.InfoHash.Should().Be("95863294e0c5a182fc358c4c09c9dea26330df71");
        first.SizeBytes.Should().Be(1_335_637_818);
        first.Seeders.Should().Be(466);
        first.Leechers.Should().Be(128);
        first.PublishedUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_410_637_473));
        first.IndexerId.Should().Be("torrents-csv");
    }

    [Fact]
    public void TorrentsCsv_Empty_ReturnsNoRows()
    {
        TorrentsCsvIndexer.Parse(Fixture("torrents-csv", "empty.json")).Should().BeEmpty();
    }

    [Fact]
    public void TorrentsCsv_UnexpectedShape_Throws()
    {
        var parse = () => TorrentsCsvIndexer.Parse("""{"error":"maintenance"}""");
        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void Bt4g_ParsesRssMagnetSizeDateAndCategory()
    {
        var rows = Bt4gIndexer.Parse(Fixture("bt4g", "coherence.xml"));

        rows.Should().HaveCount(15);
        rows.Should().OnlyContain(row => row.Seeders == 0 && row.Leechers == 0);
        var first = rows[0];
        first.Title.Should().Be("Cindy Blackman Santana - Coherence (2026, Mack Avenue)");
        first.InfoHash.Should().Be("df4073daa85a8381e363538c2380f81d8f5cdba0");
        first.MagnetUri.Should().StartWith("magnet:?xt=urn:btih:df4073daa85a8381e363538c2380f81d8f5cdba0&dn=");
        first.SizeBytes.Should().BeCloseTo((long)(545.40 * 1024 * 1024), 1024);
        first.PublishedUtc.Should().Be(new DateTimeOffset(2026, 11, 2, 18, 24, 57, TimeSpan.Zero));
        first.DetailsUrl.Should().Be("https://bt4gprx.com/magnet/V9JnwInRkJx4qnEnvuJzWv06UxUDghjP");
        first.Source.Should().Be("bt4g:Audio");

        var movie = rows.Single(row => row.Title == "Coherence (2014)");
        movie.InfoHash.Should().Be("f2be4bbe91c0d620676ed23666fed6ba398790b4");
        movie.SizeBytes.Should().BeCloseTo((long)(1.25 * 1024 * 1024 * 1024), 1024 * 1024);
        movie.Source.Should().Be("bt4g:Movie");
    }

    [Fact]
    public void Bt4g_Empty_ReturnsNoRows()
    {
        Bt4gIndexer.Parse(Fixture("bt4g", "empty.xml")).Should().BeEmpty();
    }

    [Fact]
    public void Bt4g_HtmlPage_Throws()
    {
        var parse = () => Bt4gIndexer.Parse("<html><body><p>Service unavailable<br></p></body></html>");
        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void Bitsearch_ParsesResultCards()
    {
        var rows = BitsearchIndexer.Parse(Fixture("bitsearch", "coherence.html"));

        rows.Should().HaveCount(20);
        rows.Should().OnlyContain(row => IndexerResultNormalizer.IsValidInfoHash(row.InfoHash));
        var first = rows[0];
        first.Title.Should().Be("Coherence (2013) [1080p]");
        first.InfoHash.Should().Be("95863294E0C5A182FC358C4C09C9DEA26330DF71");
        first.MagnetUri.Should().StartWith("magnet:?xt=urn:btih:95863294E0C5A182FC358C4C09C9DEA26330DF71&dn=");
        first.SizeBytes.Should().BeCloseTo((long)(1.24 * 1024 * 1024 * 1024), 1024 * 1024);
        first.Seeders.Should().Be(211);
        first.Leechers.Should().Be(256);
        first.PublishedUtc.Should().Be(new DateTimeOffset(2018, 10, 20, 0, 0, 0, TimeSpan.Zero));
        first.DetailsUrl.Should().Be("https://bitsearch.eu/torrent/5bcb6cddb81f4d084b34e85a");
        first.Source.Should().Be("bitsearch:verified");

        var remux = rows.Single(row => row.Title == "Coherence.2013.1080p.BluRay.REMUX.AVC.DTS-HD.MA");
        remux.InfoHash.Should().Be("BDBFE3D5C79E15E4EDC479807551D4179FD62839");
        remux.Seeders.Should().Be(0);
        remux.SizeBytes.Should().BeCloseTo((long)(16.65 * 1024 * 1024 * 1024), 1024 * 1024);
        remux.Source.Should().Be("bitsearch");
    }

    [Fact]
    public void Bitsearch_NoResultPage_ReturnsNoRowsWithoutThrowing()
    {
        BitsearchIndexer.Parse(Fixture("bitsearch", "empty.html")).Should().BeEmpty();
    }

    [Fact]
    public void Bitsearch_UnexpectedPage_Throws()
    {
        var parse = () => BitsearchIndexer.Parse("<html><title>Bitsearch</title><body>Maintenance</body></html>");
        parse.Should().Throw<FormatException>();
    }
}
