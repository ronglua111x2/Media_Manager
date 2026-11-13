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

    // Audit R2-B-03: a DVD resolution ("720x480") is not season 720, episode 480.
    [Theory]
    [InlineData("[Group] Show - 05 [DVD 720x480 x264].mkv")]
    [InlineData("Show.Name.05.848x480.DVDRip.mkv")]
    [InlineData("Show Name - 05 (640x480).avi")]
    public void Parse_DvdResolution_IsNotASeasonEpisode(string fileName)
    {
        var result = TorrentCandidateParser.Parse(fileName);

        result.SeasonNumber.Should().NotBe(720);
        result.SeasonNumber.Should().NotBe(848);
        result.SeasonNumber.Should().NotBe(640);
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
    public void TryParseBareEpisodeIndex_DashNumberBeforeReleaseTags_ParsesEpisode()
    {
        TorrentCandidateParser.TryParseBareEpisodeIndex("[Grp] Show - 10 [0000000A].mkv").Should().Be(10);
        TorrentCandidateParser.TryParseBareEpisodeIndex("[Grp] Show - 05 (1080p) [ABCD1234].mkv").Should().Be(5);
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

    // PARSE-06: multi-episode files keep their first episode and report the last one.
    [Theory]
    [InlineData("Show.Name.S01E01E02.1080p.mkv", 1, 1, 2)]
    [InlineData("Show.Name.S01E01-E02.1080p.mkv", 1, 1, 2)]
    [InlineData("Show.Name.S01E01-02.1080p.mkv", 1, 1, 2)]
    [InlineData("Show.Name.S02E09-E10.1080p.mkv", 2, 9, 10)]
    [InlineData("Show.Name.S01E01E02E03.mkv", 1, 1, 3)]
    [InlineData("Show.Name.S01E01-E02v2.mkv", 1, 1, 2)]
    [InlineData("Show Name S01E01 E02 Two Part Title.mkv", 1, 1, 2)]
    [InlineData("Show.Name.S01E05.1080p.mkv", 1, 5, null)]
    [InlineData("Show.Name.S01E05-1080p.mkv", 1, 5, null)]
    [InlineData("Show.Name.S01E05-2019.mkv", 1, 5, null)]
    [InlineData("Show.Name.S01E05-03.mkv", 1, 5, null)]
    public void Parse_MultiEpisode_ReportsLastEpisode(string fileName, int season, int episode, int? lastEpisode)
    {
        var result = TorrentCandidateParser.Parse(fileName);

        result.SeasonNumber.Should().Be(season);
        result.EpisodeNumber.Should().Be(episode);
        result.LastEpisodeNumber.Should().Be(lastEpisode);
    }

    [Fact]
    public void Parse_MultiEpisode_KeepsEpisodeTitleClean()
    {
        var single = TorrentCandidateParser.Parse("Show.Name.S01E01.The.Pilot.1080p.mkv").EpisodeTitle;

        TorrentCandidateParser.Parse("Show.Name.S01E01-E02.The.Pilot.1080p.mkv").EpisodeTitle.Should().Be(single);
        TorrentCandidateParser.Parse("Show.Name.S01E01-02.The.Pilot.1080p.mkv").EpisodeTitle.Should().Be(single);
        single.Should().StartWith("The Pilot");
    }

    // Parser audit 2026-11, report section 2: the 35 hard cases run through the same calls the
    // pack grouper, special bucket detector and single-episode file picker use. Each row states the
    // desired result; rows still broken carry Skip = the finding that fixes them.
    // Columns: case number, relative path in the torrent, Parse(fileName, relativePath) season/episode,
    // Parse(..., allowAnimeAbsolute: true) season/episode, IsSpecialContent, IsExtraContent,
    // PackSeasonFileGrouper exclude reason, special-bucket candidate, path season hint.
    [Theory]
    [InlineData(1, "Show.Name.S01E05.1080p.WEB-DL.x264-GRP.mkv", 1, 5, 1, 5, false, false, null, false, null)]
    [InlineData(2, "Show.Name.S01E05v2.1080p.mkv", 1, 5, 1, 5, false, false, null, false, null)]
    [InlineData(3, "Show.Name.S01E01E02.1080p.mkv", 1, 1, 1, 1, false, false, null, false, null)]
    [InlineData(4, "Show.Name.S01E01-E02.1080p.mkv", 1, 1, 1, 1, false, false, null, false, null)]
    [InlineData(5, "Show.Name.S01E01-02.1080p.mkv", 1, 1, 1, 1, false, false, null, false, null)]
    [InlineData(6, "Show.Name.1x05.HDTV.mkv", 1, 5, 1, 5, false, false, null, false, null)]
    // "Season N Episode M" is one episode (cross-review 2, F2); the pack flow also reads it through Sonarr.
    [InlineData(7, "Show Name Season 1 Episode 5.mkv", 1, 5, 1, 5, false, false, null, false, null)]
    [InlineData(8, "Show.Name.S01E05.PROPER.1080p.mkv", 1, 5, 1, 5, false, false, null, false, null)]
    [InlineData(9, "Show.Name.S01E05.REPACK.1080p.mkv", 1, 5, 1, 5, false, false, null, false, null)]
    [InlineData(10, "[SubsPlease] Show - 05 (1080p) [ABCD1234].mkv", null, null, null, 5, false, false, null, false, null)]
    [InlineData(11, "[Group] Show - 05v2 [1080p].mkv", null, null, null, 5, false, false, null, false, null)]
    // Rows 12-15: absolute parse used by search/hunt (report section 8), owned by feat/native-search.
    // Row 12 is solved by SeasonDashEpisodeRegex ("S2 - 05" -> S02E05); rows 13-15 are still open.
    [InlineData(12, "[SubsPlease] Show S2 - 05 (1080p) [ABCD1234].mkv", 2, 5, 2, 5, false, false, null, false, null)]
    [InlineData(13, "[SubsPlease] Show 100 - 05 (1080p).mkv", null, null, null, 5, false, false, null, false, null, Skip = "Report section 8: still open after native-search merge")]
    [InlineData(14, "[Erai-raws] 86 - Show - 05 [1080p].mkv", null, null, null, 5, false, false, null, false, null, Skip = "Report section 8: still open after native-search merge")]
    [InlineData(15, "[SubsPlease] Show No. 8 - 05 (1080p).mkv", null, null, null, 5, false, false, null, false, null, Skip = "Report section 8: still open after native-search merge")]
    [InlineData(16, "[Group] 番組名 - 05 [1080p].mkv", null, null, null, 5, false, false, null, false, null)]
    [InlineData(17, "番組名.S01E05.1080p.mkv", 1, 5, 1, 5, false, false, null, false, null)]
    [InlineData(18, "2049.S01E03.1080p.WEB.mkv", 1, 3, 1, 3, false, false, null, false, null)]
    [InlineData(19, "Show.2005.S02E05.1080p.mkv", 2, 5, 2, 5, false, false, null, false, null)]
    [InlineData(20, "Show.2005.S00E05.Christmas.Special.mkv", 0, 5, 0, 5, true, false, null, false, null)]
    [InlineData(21, "Daily.Show.2026.11.10.Guest.1080p.mkv", null, null, null, null, false, false, null, false, null)]
    [InlineData(22, "Special Ops Show/Season 1/Special.Ops.Show.S01E03.1080p.mkv", 1, 3, 1, 3, false, false, null, false, 1)]
    [InlineData(23, "Show/Season 2/Show.S02E07.A.Very.Special.Episode.1080p.mkv", 2, 7, 2, 7, false, false, null, false, 2)]
    [InlineData(24, "Show/Season 3/Show.S03E04.Movie.Night.1080p.mkv", 3, 4, 3, 4, false, false, null, false, 3)]
    [InlineData(25, "Moviepass Show/Season 1/Moviepass.Show.S01E02.mkv", 1, 2, 1, 2, false, false, null, false, 1)]
    [InlineData(26, "Show/Season 1/OVA/Show.OVA.02.mkv", null, null, null, 2, true, false, "special-bucket", true, 1)]
    [InlineData(27, "Show/Season 1/Show.S01S02.mkv", null, null, null, null, true, false, "special-bucket", true, 1)]
    [InlineData(28, "Show/Season 1/Extras/Show.NCOP.01.mkv", null, null, null, 1, false, true, "extras-folder", false, 1)]
    [InlineData(29, "Show/Show.NCOP01.mkv", null, null, null, null, false, true, "extra-content", false, null)]
    [InlineData(30, "Show.S01E05.1080p/Sample/show.s01e05.1080p-sample.mkv", 1, 5, 1, 5, false, false, "sample", false, null)]
    [InlineData(31, "Show/Season 1 Part 2/Show.S01E03.mkv", 1, 3, 1, 3, false, false, null, false, null)]
    [InlineData(32, "Show Season 1-3 1080p/Show.S02E05.1080p.mkv", 2, 5, 2, 5, false, false, null, false, null)]
    // Rows 33-35: the episode is only in a folder name or needs Sonarr; this parser leaves them alone.
    [InlineData(33, "Show.S01.1080p.WEB/Show.E05.1080p.mkv", null, null, null, null, false, false, null, false, null)]
    [InlineData(34, "Show.S01E05.1080p.WEB/abc123.mkv", null, null, null, null, false, false, null, false, null)]
    [InlineData(35, "Show.Name.S01.720x480.DVDRip/Show.Name.05.mkv", null, null, null, 5, false, false, null, false, null)]
    public void Parse_AuditHardCase_MatchesDesiredResult(
        int caseNumber,
        string relativePath,
        int? season,
        int? episode,
        int? absoluteSeason,
        int? absoluteEpisode,
        bool isSpecial,
        bool isExtra,
        string? excludeReason,
        bool isSpecialBucket,
        int? pathSeasonHint)
    {
        var fileName = Path.GetFileName(relativePath);

        var parsed = TorrentCandidateParser.Parse(fileName, relativePath);
        var parsedAbsolute = TorrentCandidateParser.Parse(fileName, relativePath, allowAnimeAbsolute: true);

        parsed.SeasonNumber.Should().Be(season, $"case {caseNumber} season");
        parsed.EpisodeNumber.Should().Be(episode, $"case {caseNumber} episode");
        parsedAbsolute.SeasonNumber.Should().Be(absoluteSeason, $"case {caseNumber} season (absolute allowed)");
        parsedAbsolute.EpisodeNumber.Should().Be(absoluteEpisode, $"case {caseNumber} episode (absolute allowed)");
        parsed.IsSpecialContent.Should().Be(isSpecial, $"case {caseNumber} special flag");
        parsed.IsExtraContent.Should().Be(isExtra, $"case {caseNumber} extra flag");
        PackSeasonFileGrouper.GetEpisodeGroupExcludeReason(relativePath, fileName)
            .Should().Be(excludeReason, $"case {caseNumber} exclude reason");
        PackSpecialBucketDetector.IsSpecialBucketCandidate(relativePath, fileName)
            .Should().Be(isSpecialBucket, $"case {caseNumber} special bucket");
        TorrentCandidateParser.TryGetSeasonHintFromPath(relativePath)
            .Should().Be(pathSeasonHint, $"case {caseNumber} path season hint");
    }
}
