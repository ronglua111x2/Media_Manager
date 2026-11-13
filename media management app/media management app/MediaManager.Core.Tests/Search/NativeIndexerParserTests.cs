using FluentAssertions;
using media_management_app.Search;
using media_management_app.Search.Indexers;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Search;

/// <summary>Parsers against real responses captured 12/11/2026 (Fixtures/Indexers/&lt;id&gt;/).</summary>
public class NativeIndexerParserTests
{
    private static string Fixture(string indexer, string file) =>
        File.ReadAllText(GoldenFixtureFile.PathTo(Path.Combine("Indexers", indexer, file)));

    [Fact]
    public void Knaben_ParsesHitsWithHashMagnetSeedersAndTracker()
    {
        var rows = KnabenIndexer.Parse(Fixture("knaben", "coherence.json"));

        rows.Should().HaveCount(20);
        var first = rows[0];
        first.Title.Should().Be("Coherence (2013) 1080p BrRip x264 - YIFY");
        first.InfoHash.Should().Be("95863294E0C5A182FC358C4C09C9DEA26330DF71");
        first.MagnetUri.Should().StartWith("magnet:?xt=urn:btih:95863294");
        first.SizeBytes.Should().Be(1_335_637_818);
        first.Seeders.Should().Be(384);
        first.Source.Should().Be("The Pirate Bay");
        first.VirusProbability.Should().BeLessThan(0.5);
    }

    [Fact]
    public void Knaben_Empty_ReturnsNoRows()
    {
        KnabenIndexer.Parse(Fixture("knaben", "empty.json")).Should().BeEmpty();
    }

    [Fact]
    public void Apibay_ParsesRowsAndBuildsMagnetFromHash()
    {
        var rows = ApibayIndexer.Parse(Fixture("apibay", "coherence.json"));

        rows.Should().NotBeEmpty();
        var yify = rows.First(row => row.Title == "Coherence (2013) 1080p BrRip x264 - YIFY");
        yify.InfoHash.Should().Be("95863294E0C5A182FC358C4C09C9DEA26330DF71");
        yify.Seeders.Should().Be(375);
        yify.SizeBytes.Should().Be(1_335_637_818);
        yify.Source.Should().Be("tpb:vip");

        var normalized = IndexerResultNormalizer.ToSearchResult(yify, "thepiratebay");
        normalized.FileUrl.Should().StartWith("magnet:?xt=urn:btih:95863294e0c5a182fc358c4c09c9dea26330df71");
        normalized.EngineName.Should().Be("thepiratebay");
    }

    [Fact]
    public void Apibay_NoResultsRow_IsIgnored()
    {
        ApibayIndexer.Parse(Fixture("apibay", "empty.json")).Should().BeEmpty();
    }

    [Fact]
    public void Nyaa_ParsesRssWithNamespaceFields()
    {
        var rows = NyaaIndexer.Parse(Fixture("nyaa", "kanata-kara.xml"));

        rows.Should().NotBeEmpty();
        var first = rows[0];
        first.Title.Should().Be("[Erai-raws] Kanata kara - 06 [1080p CR WEBRip HEVC AAC][MultiSub][478047D0]");
        first.InfoHash.Should().Be("52b6953d6a38b99ccc81ea959f9df31d4edc3229");
        first.Seeders.Should().Be(66);
        first.Leechers.Should().Be(6);
        first.SizeBytes.Should().BeCloseTo((long)(430.5 * 1024 * 1024), 1024);
        first.PublishedUtc.Should().Be(new DateTimeOffset(2026, 11, 9, 3, 19, 53, TimeSpan.Zero));
        first.Source.Should().Be("nyaa:trusted");
    }

    [Fact]
    public void Nyaa_Empty_ReturnsNoRows()
    {
        NyaaIndexer.Parse(Fixture("nyaa", "empty.xml")).Should().BeEmpty();
    }

    [Fact]
    public void LimeTorrents_ParsesResultRowsAndSkipsAdRows()
    {
        var rows = LimeTorrentsIndexer.Parse(Fixture("limetorrents", "coherence.html"));

        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(row => IndexerResultNormalizer.IsValidInfoHash(row.InfoHash));
        rows.Should().NotContain(row => row.Title.Contains("Download", StringComparison.Ordinal) && row.Title.Length < 25);
        var yify = rows.First(row => row.Title == "Coherence (2013) 1080p BrRip x264 - YIFY");
        yify.InfoHash.Should().Be("95863294E0C5A182FC358C4C09C9DEA26330DF71");
        yify.Seeders.Should().Be(448);
        yify.Leechers.Should().Be(159);
        yify.SizeBytes.Should().BeCloseTo((long)(1.24 * 1024 * 1024 * 1024), 1024 * 1024);
    }

    [Fact]
    public void LimeTorrents_NoResultPage_ReturnsNoRowsWithoutThrowing()
    {
        LimeTorrentsIndexer.Parse(Fixture("limetorrents", "empty.html")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("1.24 GB", 1_331_439_861L)]
    [InlineData("430.5 MiB", 451_411_968L)]
    [InlineData("700 MB", 734_003_200L)]
    [InlineData("1,5 GB", 1_610_612_736L)]
    [InlineData("1,024.5 MB", 1_074_266_112L)]
    [InlineData("1,234 MB", 1_293_942_784L)]
    [InlineData("", 0L)]
    public void ParseSize_UsesBinaryUnits(string text, long expected)
    {
        var rows = NyaaIndexer.Parse($"""<rss xmlns:nyaa="https://nyaa.si/xmlns/nyaa"><channel><item><title>x</title><nyaa:size>{text}</nyaa:size></item></channel></rss>""");
        rows[0].SizeBytes.Should().BeCloseTo(expected, 2);
    }

    [Fact]
    public void ChallengePage_IsDetected_ButJsonIsNot()
    {
        ChallengeDetector.LooksLikeChallenge("<html><title>Just a moment...</title>", "text/html").Should().BeTrue();
        ChallengeDetector.LooksLikeChallenge("<title>One moment, please</title>", "text/html").Should().BeTrue();
        ChallengeDetector.LooksLikeChallenge("{\"captcha\":false}", "application/json").Should().BeFalse();
        ChallengeDetector.LooksLikeChallenge(Fixture("limetorrents", "coherence.html"), "text/html").Should().BeFalse();
    }
}
