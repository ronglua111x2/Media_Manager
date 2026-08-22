using FluentAssertions;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Torrent;

public class TorrentCandidateParserTests
{
    [Fact]
    public void Parse_SxxExx_DottedTitle_ParsesSeasonAndEpisode()
    {
        var result = TorrentCandidateParser.Parse(
            "Show.Name.S02E05.1080p.WEB-DL.x264-GROUP.mkv");

        result.SeasonNumber.Should().Be(2);
        result.EpisodeNumber.Should().Be(5);
        result.Quality.Should().Be("1080p");
    }

    [Fact]
    public void Parse_OneXZeroOneFormat_ParsesSeasonAndEpisode()
    {
        var result = TorrentCandidateParser.Parse("Some.Anime.2x07.720p.HDTV.x264");

        result.SeasonNumber.Should().Be(2);
        result.EpisodeNumber.Should().Be(7);
        result.Quality.Should().Be("720p");
    }

    [Fact]
    public void Parse_RezaMinimalS01E01_ParsesEpisode()
    {
        var result = TorrentCandidateParser.Parse(
            "[Reza] Smoking Behind the Supermarket with You - S01E01.mkv");

        result.SeasonNumber.Should().Be(1);
        result.EpisodeNumber.Should().Be(1);
    }

    [Fact]
    public void Parse_JujutsuKaisenBracketGroup_ParsesS03E01()
    {
        var result = TorrentCandidateParser.Parse(
            "[Subeteka] Jujutsu Kaisen - S03E01 [1080p WEB DUAL DDP2.0 H.265] [7BE3B471].mkv");

        result.SeasonNumber.Should().Be(3);
        result.EpisodeNumber.Should().Be(1);
        result.Quality.Should().Be("1080p");
    }

    [Fact]
    public void Parse_SpaceSeparatedTitle_ParsesS03E11()
    {
        var result = TorrentCandidateParser.Parse(
            "JUJUTSU KAISEN S03E11 Tokyo Colony No 1 Part 5 1080p CR WEB-DL DDP2 0 H 264-Kitsune");

        result.SeasonNumber.Should().Be(3);
        result.EpisodeNumber.Should().Be(11);
        result.Quality.Should().Be("1080p");
    }

    [Fact]
    public void Parse_AbsoluteAnime_WhenAllowed_ParsesEpisodeNumber()
    {
        var result = TorrentCandidateParser.Parse(
            "[Erai-raws] Tai Ari Deshita Ojou-sama wa Kakutou Game Nante Shinai - 07 [1080p CR WEB-DL AVC AAC][MultiSub][0FBE586A].mkv",
            allowAnimeAbsolute: true);

        result.AbsoluteEpisodeNumber.Should().Be(7);
        result.EpisodeNumber.Should().Be(7);
        result.SeasonNumber.Should().BeNull();
        result.Quality.Should().Be("1080p");
    }

    [Fact]
    public void Parse_AbsoluteAnime_WhenNotAllowed_DoesNotParseBareDigits()
    {
        var fileName =
            "[Erai-raws] taiari - 02 [1080p CR WEB-DL AVC AAC][MultiSub][350457AB].mkv";

        var withoutAbsolute = TorrentCandidateParser.Parse(fileName, allowAnimeAbsolute: false);
        var withAbsolute = TorrentCandidateParser.Parse(fileName, allowAnimeAbsolute: true);

        withoutAbsolute.AbsoluteEpisodeNumber.Should().BeNull();
        withAbsolute.AbsoluteEpisodeNumber.Should().Be(2);
    }

    [Fact]
    public void Parse_SeasonPack_ExtractsCoveredSeasons()
    {
        var result = TorrentCandidateParser.Parse(
            "Anime Title S01-S02 Complete Batch 1080p WEB-DL");

        result.CoveredSeasons.Should().BeEquivalentTo([1, 2]);
        result.Quality.Should().Be("1080p");
        result.SeasonNumber.Should().BeNull();
        result.EpisodeNumber.Should().BeNull();
    }

    [Fact]
    public void Parse_SeasonFolderName_ParsesSeasonSix()
    {
        var result = TorrentCandidateParser.Parse(
            "BoJack Horseman (2014) Season 6 S06 (1080p NF WEB-DL x265 HEVC 10bit EAC3 5.1 Ghost)");

        result.CoveredSeasons.Should().Contain(6);
        result.ExplicitYear.Should().Be(2014);
        result.Quality.Should().Be("1080p");
    }

    [Fact]
    public void Parse_OvaSpecialWithYear_DetectsSpecialContent()
    {
        var result = TorrentCandidateParser.Parse(
            "[Okay-Subs] Hibike! Euphonium Ensemble Contest (2023) (BD 1080p) [F67CD190].mkv");

        result.ExplicitYear.Should().Be(2023);
        result.Quality.Should().Be("1080p");
    }

    [Fact]
    public void Parse_OvaBeforeEpisode_MarksSpecialWithSeasonHint()
    {
        var result = TorrentCandidateParser.Parse(
            "Show Name OVA S02E03 1080p WEB-DL.mkv",
            relativePath: "Specials/Show Name OVA S02E03 1080p WEB-DL.mkv");

        result.IsSpecialContent.Should().BeTrue();
        result.ReleaseSeasonHint.Should().Be(2);
        result.ReleaseSpecialIndex.Should().Be(3);
        result.PreferEpisodeIndexMatch.Should().BeTrue();
    }

    [Fact]
    public void Parse_MovieWithYear_ExtractsYearAnd2160pQuality()
    {
        var result = TorrentCandidateParser.Parse(
            "Your.Name.2016.2160p.BluRay.x265.10bit.HDR.DTS-HD.MA.5.1-SWTYBLZ");

        result.ExplicitYear.Should().Be(2016);
        result.Quality.Should().Be("2160p");
        result.SeasonNumber.Should().BeNull();
        result.EpisodeNumber.Should().BeNull();
    }

    [Fact]
    public void Parse_RemuxTag_Detects2160pAndAudioCodec()
    {
        var result = TorrentCandidateParser.Parse(
            "Oppenheimer.2023.2160p.REMUX.IMAX.Dolby.Vision.And.HDR10.PLUS.ENG.ITA.LATINO.DTS-HD.Master.DDP5.1.DV.x265.MKV-BEN.THE.MEN");

        result.Quality.Should().Be("2160p");
        result.AudioCodec.Should().Be("DTS-HD");
        result.ExplicitYear.Should().Be(2023);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyResult()
    {
        var result = TorrentCandidateParser.Parse(string.Empty);

        result.RawTitle.Should().BeEmpty();
        result.NormalizedTitle.Should().BeEmpty();
        result.SeasonNumber.Should().BeNull();
        result.EpisodeNumber.Should().BeNull();
    }

    [Fact]
    public void ContainsYearRangeIncluding_WhenYearInRange_ReturnsTrue()
    {
        TorrentCandidateParser.ContainsYearRangeIncluding("Show 2019-2021 Complete", 2020)
            .Should().BeTrue();
    }

    [Fact]
    public void TryGetSeasonHintFromPath_SeasonFolder_ReturnsSeason()
    {
        TorrentCandidateParser.TryGetSeasonHintFromPath(@"Show\Season 2\Episode.mkv")
            .Should().Be(2);
    }

    [Fact]
    public void TryParseBareEpisodeIndex_DashSuffix_ParsesEpisode()
    {
        TorrentCandidateParser.TryParseBareEpisodeIndex("Special Title - 04.mkv")
            .Should().Be(4);
    }

    [Fact]
    public void Parse_ExtrasFolder_MarksExtraContent()
    {
        var result = TorrentCandidateParser.Parse(
            "NCED.mkv",
            relativePath: "Show/Extras/NCED.mkv");

        result.IsExtraContent.Should().BeTrue();
        result.IsSpecialContent.Should().BeFalse();
    }

    [Fact]
    public void Parse_RepackTag_StillParsesEpisode()
    {
        var result = TorrentCandidateParser.Parse(
            "Jujutsu Kaisen S03E02 One More Time REPACK 1080p CR WEB-DL AAC2 0 H 264-playWEB");

        result.SeasonNumber.Should().Be(3);
        result.EpisodeNumber.Should().Be(2);
        result.Quality.Should().Be("1080p");
    }

    [Fact]
    public void Parse_NormalizesTitleTokens_ForMatching()
    {
        var result = TorrentCandidateParser.Parse(
            "Chainsmoker.Cat.S01E08.1080p.NF.WEB-DL.JPN.AAC2.0.H.264.MSubs-ToonsHub.mkv");

        result.TitleTokens.Should().Contain("Chainsmoker");
        result.TitleTokens.Should().Contain("Cat");
    }
}
