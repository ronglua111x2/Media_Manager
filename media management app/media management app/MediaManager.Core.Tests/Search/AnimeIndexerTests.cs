using FluentAssertions;
using media_management_app.Search;
using media_management_app.Search.Indexers;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Search;

/// <summary>Anime indexer parsers against real responses captured 12/11/2026 (Fixtures/Indexers/&lt;id&gt;/).</summary>
public class AnimeIndexerTests
{
    private static string Fixture(string indexer, string file) =>
        File.ReadAllText(GoldenFixtureFile.PathTo(Path.Combine("Indexers", indexer, file)));

    [Fact]
    public void AnimeIndexers_KeepOldPluginEngineNamesAndAnimeCapability()
    {
        ITorrentIndexer[] indexers = [new TsukiHimeIndexer(), new SubsPleaseIndexer(), new NekoBtIndexer(), new AnimeToshoIndexer()];

        indexers.Select(indexer => (indexer.Id, indexer.EngineName)).Should().Equal(
            ("tsukihime", "tsukihime"),
            ("subsplease", "subsplease"),
            ("nekobt", "nekobt"),
            ("animetosho", "animetosho"));
        indexers.Should().OnlyContain(indexer => indexer.Capabilities.Kinds == IndexerMediaKinds.Anime);
    }

    [Fact]
    public void TsukiHime_ParsesNameHashSizeAndSourceDate()
    {
        var rows = TsukiHimeIndexer.Parse(Fixture("tsukihime", "kanata-kara.json"));

        rows.Should().HaveCount(50);
        var first = rows[0];
        first.Title.Should().Be("[Erai-raws] Kanata kara - 06 [1080p CR WEBRip HEVC AAC][MultiSub][478047D0]");
        first.InfoHash.Should().Be("52b6953d6a38b99ccc81ea959f9df31d4edc3229");
        first.MagnetUri.Should().BeNull();
        first.SizeBytes.Should().Be(451_364_450);
        first.Seeders.Should().Be(0);
        first.PublishedUtc.Should().Be(new DateTimeOffset(2026, 11, 9, 3, 19, 53, TimeSpan.Zero));
        first.DetailsUrl.Should().Be("https://nyaa.si/view/2184228");

        var normalized = IndexerResultNormalizer.ToSearchResult(first, "tsukihime");
        normalized.FileUrl.Should().StartWith("magnet:?xt=urn:btih:52b6953d6a38b99ccc81ea959f9df31d4edc3229");
    }

    [Fact]
    public void TsukiHime_Empty_ReturnsNoRows()
    {
        TsukiHimeIndexer.Parse(Fixture("tsukihime", "empty.json")).Should().BeEmpty();
    }

    [Fact]
    public void SubsPlease_EmitsOneRowPerResolutionWithBase32HashConverted()
    {
        var rows = SubsPleaseIndexer.Parse(Fixture("subsplease", "kanata-kara.json"));

        rows.Should().HaveCount(21);
        var first = rows[0];
        first.Title.Should().Be("[SubsPlease] Kanata kara - 06 (480p)");
        first.InfoHash.Should().Be("f1acfdc8c3fa5e713d1a6ddd0fd6651ed24debca");
        first.MagnetUri.Should().StartWith("magnet:?xt=urn:btih:6GWP3SGD7JPHCPI2NXOQ7VTFD3JE326K");
        first.SizeBytes.Should().Be(383_100_044);
        first.Seeders.Should().Be(0);
        first.PublishedUtc.Should().Be(new DateTimeOffset(2026, 11, 8, 15, 3, 30, TimeSpan.Zero));
        first.DetailsUrl.Should().Be("https://subsplease.org/shows/kanata-kara/");

        var fullHd = rows[2];
        fullHd.Title.Should().Be("[SubsPlease] Kanata kara - 06 (1080p)");
        fullHd.InfoHash.Should().Be("b83d868df20bd7ba36f2e13c239edb5966ee69ce");
        fullHd.SizeBytes.Should().Be(1_448_198_620);

        rows.Should().Contain(row => row.Title == "[SubsPlease] Kanata kara - 01v2 (720p)");
    }

    [Fact]
    public void SubsPlease_Empty_ReturnsNoRows()
    {
        SubsPleaseIndexer.Parse(Fixture("subsplease", "empty.json")).Should().BeEmpty();
    }

    [Fact]
    public void SubsPlease_UnexpectedShape_Throws()
    {
        var act = () => SubsPleaseIndexer.Parse("<html>maintenance</html>");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void NekoBt_ParsesStringCountsMagnetAndMillisecondDate()
    {
        var rows = NekoBtIndexer.Parse(Fixture("nekobt", "kanata-kara.json"));

        rows.Should().HaveCount(50);
        var first = rows[0];
        first.Title.Should().Be("[Erai-raws] Kanata kara - 06 [1080p CR WEBRip HEVC AAC][MultiSub][478047D0]");
        first.InfoHash.Should().Be("52b6953d6a38b99ccc81ea959f9df31d4edc3229");
        first.MagnetUri.Should().StartWith("magnet:?xt=urn:btih:52b6953d6a38b99ccc81ea959f9df31d4edc3229");
        first.SizeBytes.Should().Be(451_364_450);
        first.Seeders.Should().Be(43);
        first.Leechers.Should().Be(1);
        first.PublishedUtc.Should().Be(new DateTimeOffset(2026, 11, 9, 3, 19, 53, 839, TimeSpan.Zero));
        first.DetailsUrl.Should().Be("https://nekobt.to/torrents/14977227222792");
    }

    [Fact]
    public void NekoBt_Empty_ReturnsNoRows()
    {
        NekoBtIndexer.Parse(Fixture("nekobt", "empty.json")).Should().BeEmpty();
    }

    [Fact]
    public void AnimeTosho_ParsesHtmlEntriesWithExactSizeSwarmAndMagnet()
    {
        var rows = AnimeToshoIndexer.Parse(Fixture("animetosho", "frieren.html"));

        rows.Should().HaveCount(75);
        var first = rows[0];
        first.Title.Should().Be("[9volt] Sousou no Frieren - Season 2 (WEB 1080p HEVC EAC-3 Dual Audio) | Frieren: Beyond Journey's End (2026) (Batch S02 S2)");
        first.InfoHash.Should().Be("2fd2133f28fbd29021b775c17c8e1cf5aceb9a15");
        first.MagnetUri.Should().StartWith("magnet:?xt=urn:btih:F7JBGPZI7PJJAINXOXAXZDQ46WWOXGQV&tr=");
        first.MagnetUri.Should().NotContain("&amp;");
        first.SizeBytes.Should().Be(33_343_733_854);
        first.Seeders.Should().Be(1);
        first.Leechers.Should().Be(242);
        first.PublishedUtc.Should().Be(new DateTimeOffset(2026, 5, 5, 20, 50, 0, TimeSpan.Zero));
        first.TorrentUrl.Should().Be("https://nyaa.si/download/2106580.torrent");
        first.DetailsUrl.Should().Be("https://animetosho.org/view/9volt-sousou-no-frieren-season-2-web-1080p.n2106580");
    }

    [Fact]
    public void AnimeTosho_Empty_ReturnsNoRows()
    {
        AnimeToshoIndexer.Parse(Fixture("animetosho", "empty.html")).Should().BeEmpty();
    }

    [Fact]
    public void AnimeTosho_UnknownLayout_Throws()
    {
        var act = () => AnimeToshoIndexer.Parse("<html><body>Just a moment...</body></html>");

        act.Should().Throw<FormatException>();
    }
}
