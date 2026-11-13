using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Search;

/// <summary>native-search P1 / SRCH-11. Release names are real rows returned by public engines (12/11/2026).</summary>
public class YearMatcherTests
{
    private const int Now = 2026;

    [Theory]
    [InlineData("Coherence (2013) 1080p BrRip x264 - YIFY")]
    [InlineData("Coherence (2013) (1080p BluRay x265 HEVC 10bit AAC 5 1 MONOLITH)")]
    [InlineData("Coherence 2013 BluRay 1080p DTS-HD MA 5 1 AVC REMUX-ROLAND")]
    [InlineData("Coherence 2013 1080p BluRay x264-GeneMige")]
    public void Coherence_TmdbYear2014_AcceptsReleasesTagged2013AsOffByOne(string name)
    {
        // 322 of 479 "Coherence" rows were tagged 2013, 11 tagged 2014: the old Contains("2014") rejected ~97%.
        YearMatcher.Match(2014, name, ["Coherence"], Now).Kind.Should().Be(YearMatchKind.OffByOne);
    }

    [Theory]
    [InlineData("Coherence_2014_BRRip_High mp4")]
    [InlineData("coherence-vostfr-dvdrip-2014")]
    public void ExactYear_IsExact(string name)
    {
        YearMatcher.Match(2014, name, ["Coherence"], Now).Kind.Should().Be(YearMatchKind.Exact);
    }

    [Theory]
    [InlineData("Kobayashi T  Quantal and Classical Coherence and Correlation 2025")]
    [InlineData("Cindy Blackman Santana Coherence 2026 WEB FLAC [16bit 44 1khz]- TEAM EICHBAUM")]
    [InlineData("Dubois A  Handbook of Full-Field Optical Coherence Microscopy  Tech and App 2016")]
    public void DistantYear_IsMismatch(string name)
    {
        YearMatcher.Match(2014, name, ["Coherence"], Now).IsRejected.Should().BeTrue();
    }

    [Fact]
    public void NoYear_IsMissing()
    {
        YearMatcher.Match(2014, "Coherence (microHD) SPANISH ESPANOL x264-ELITETORRENT", ["Coherence"], Now)
            .Kind.Should().Be(YearMatchKind.Missing);
    }

    [Theory]
    [InlineData("2001 A Space Odyssey (1968) [BluRay] [1080p] [YTS AM]")]
    [InlineData("2001 A Space Odyssey 1968 Remastered 1080p BluRay HEVC x265 5 1 BONE")]
    [InlineData("2001 - A Space Odyssey (1968) V2 (2160p BluRay x265 HEVC 10bit HDR AAC 5 1 Tigol..")]
    public void NumberInsideTitle_IsNotTheReleaseYear(string name)
    {
        YearMatcher.ReleaseYears(name, ["2001: A Space Odyssey"], Now).Should().Equal(1968);
        YearMatcher.Match(1968, name, ["2001: A Space Odyssey"], Now).Kind.Should().Be(YearMatchKind.Exact);
    }

    [Fact]
    public void TitleThatIsAYear_WithoutReleaseYear_IsMissing()
    {
        YearMatcher.Match(2019, "1917.1080p.BluRay.x264", ["1917"], Now).Kind.Should().Be(YearMatchKind.Missing);
        YearMatcher.Match(2019, "1917.2019.1080p.BluRay.x264", ["1917"], Now).Kind.Should().Be(YearMatchKind.Exact);
    }

    [Fact]
    public void Remake_WithSameTitle_IsRejected()
    {
        YearMatcher.Match(1984, "Dune.2021.1080p.WEB-DL.DDP5.1.Atmos", ["Dune"], Now).IsRejected.Should().BeTrue();
        YearMatcher.Match(2021, "Dune.1984.Extended.1080p.BluRay", ["Dune"], Now).IsRejected.Should().BeTrue();
    }

    [Fact]
    public void ResolutionAndFutureNumbers_AreNotYears()
    {
        YearMatcher.ReleaseYears("Movie.1080p.2160p.x264.3000.1899", ["Movie"], Now).Should().BeEmpty();
    }

    [Fact]
    public void NoExpectedYear_IsNotApplicable()
    {
        YearMatcher.Match(null, "Anything 1999", ["Anything"], Now).Kind.Should().Be(YearMatchKind.NotApplicable);
    }
}
