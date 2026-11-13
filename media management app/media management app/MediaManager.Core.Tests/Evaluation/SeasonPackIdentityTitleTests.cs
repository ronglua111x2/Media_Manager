using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Evaluation;

/// <summary>
/// Season pack identity after the native-search follow-up (2026-11-13): packs named only by an alternative title,
/// SubsPlease-style single episodes ("S3 - 13") that used to look like packs of seasons 3-13, and repeated title
/// words (07c NS-30). Names are rows of the shows-compare bench of 13/11/2026.
/// </summary>
public class SeasonPackIdentityTitleTests
{
    [Theory]
    [InlineData("This Monster Wants to Eat Me", "Watashi wo Tabetai, Hitodenashi", "Watashi wo Tabetai, Hitodenashi (Season 1) [1080p] [Amanogawa]", 1)]
    [InlineData("This Monster Wants to Eat Me", "Watashi wo Tabetai, Hitodenashi", "[InariDub] Watashi wo Tabetai Hitodenashi (2025) (Season 1) WEB-DL (1080p)", 1)]
    [InlineData("Ascendance of a Bookworm", "Honzuki no Gekokujou", "[AniPlague] Honzuki no Gekokujou S04 1080p", 4)]
    [InlineData("Otherside Picnic", "Urasekai Picnic", "[DKB] Urasekai Picnic (Season 1) [1080p][HEVC-265 10bit][Eng-Subs][batch]", 1)]
    [InlineData("Bloom Into You", "Yagate Kimi ni Naru", "[Group] Yagate Kimi ni Naru S01 1080p BD", 1)]
    [InlineData("BoJack Horseman", "", "[47BT][马男波杰克 第六季]BoJack.Horseman.S06.1080p.WEB-DL.HEVC.AAC", 6)]
    [InlineData("Hunter x Hunter", "", "Hunter x Hunter (2011) S01 1080p", 1)]
    [InlineData("Kill la Kill", "", "Kill la Kill S01 1080p BD", 1)]
    [InlineData("This Monster Wants to Eat Me", "", "【高清剧集网发布 www.TTHDTT.com】想吃掉我的非人少女[全13集][中文字幕].This.Monster.Wants.to.Eat.Me.S01.1080p.WEB-DL.AAC2.0.H.264-BlackTV", 1)]
    public void PackNamedByAnyTitle_IsAccepted(string title, string alternative, string name, int season)
    {
        var reject = Evaluate(title, alternative, name, season);

        reject.Reason.Should().Be(CandidateRejectReason.None, reject.Detail);
    }

    [Theory]
    // Another show sharing one word, counted twice before (07c NS-30)
    [InlineData("Hunter x Hunter", "", "Monster Hunter Stories S01 1080p", 1, CandidateRejectReason.TitleMismatch)]
    [InlineData("Kill la Kill", "", "Kill Me Baby S01 1080p", 1, CandidateRejectReason.TitleMismatch)]
    // Single episodes that looked like packs of seasons 3..13 / 4..17
    [InlineData("Sound! Euphonium", "Hibike! Euphonium", "[SubsPlease] Hibike! Euphonium S3 - 13 (1080p)", 3, CandidateRejectReason.WrongReleaseKind)]
    [InlineData("Ascendance of a Bookworm", "Honzuki no Gekokujou", "[SubsPlease] Honzuki no Gekokujou S4 - 17 (1080p) [E56B1187] mkv", 4, CandidateRejectReason.WrongReleaseKind)]
    [InlineData("Ascendance of a Bookworm", "Honzuki no Gekokujou", "[Erai-raws] Honzuki no Gekokujou S4 - 17v2 [1080p CR WEB-DL AVC AAC][MultiSub][BC7A5FC1] mkv", 4, CandidateRejectReason.WrongReleaseKind)]
    [InlineData("My Teen Romantic Comedy SNAFU", "OreGairu", "[JAM CLUB] OreGairu 3rd season 03 (1080p)[RUS][DUB]", 3, CandidateRejectReason.WrongReleaseKind)]
    // Episodes of another season or bracketed episode numbers, accepted as S04 packs before (shows-compare 13/11)
    [InlineData("Ascendance of a Bookworm", "Honzuki no Gekokujou", "[Anime Time] Ascendance of a Bookworm Season 3 - 04 (1080p)[HEVC 10bit x265][AAC]", 4, CandidateRejectReason.WrongReleaseKind)]
    [InlineData("Ascendance of a Bookworm", "Honzuki no Gekokujou", "[SweetSub][Honzuki no Gekokujou S04][17][WebRip][1080P][AVC 8bit]", 4, CandidateRejectReason.WrongReleaseKind)]
    [InlineData("This Monster Wants to Eat Me", "", "【高清剧集网发布 www.BBHDTV.com】想吃掉我的非人少女[第02集][中文字幕].This.Monster.Wants.to.Eat.Me.S01.1080p.WEB-DL.AAC2.0.H.264-BlackTV", 1, CandidateRejectReason.WrongReleaseKind)]
    public void OtherShowOrSingleEpisode_IsRejected(string title, string alternative, string name, int season, CandidateRejectReason expected)
    {
        var reject = Evaluate(title, alternative, name, season);

        reject.Reason.Should().Be(expected, reject.Detail);
    }

    [Fact]
    public void EpisodeBatchOfOneSeason_StaysAPackOfThatSeasonOnly()
    {
        var parsed = TorrentCandidateParser.Parse("[ASW] Honzuki no Gekokujou S4 - 01-12 [1080p HEVC]");

        parsed.CoveredSeasons.Should().Equal(4);
        TorrentReleaseKind.Classify("[ASW] Honzuki no Gekokujou S4 - 01-12 [1080p HEVC]", parsed).IsPack.Should().BeTrue();
    }

    [Theory]
    [InlineData("[SubsPlease] Hibike! Euphonium S3 - 13 (1080p)", 3, 13)]
    [InlineData("[Erai-raws] Honzuki no Gekokujou S4 - 17v2 [1080p CR WEB-DL AVC AAC]", 4, 17)]
    [InlineData("[Judas] Jujutsu Kaisen S3 - 01 [1080p]", 3, 1)]
    [InlineData("[Anime Time] Ascendance of a Bookworm Season 3 - 04 (1080p)", 3, 4)]
    public void SeasonDashEpisode_IsThatEpisode(string name, int season, int episode)
    {
        var parsed = TorrentCandidateParser.Parse(name);

        parsed.SeasonNumber.Should().Be(season);
        parsed.EpisodeNumber.Should().Be(episode);
        parsed.CoveredSeasons.Should().Equal(season);
        TorrentReleaseKind.Classify(name, parsed).IsEpisode.Should().BeTrue();
    }

    [Theory]
    [InlineData("Breaking Bad S01-S05 1080p", new[] { 1, 2, 3, 4, 5 })]
    [InlineData("Breaking Bad S1-3 1080p", new[] { 1, 2, 3 })]
    [InlineData("Breaking Bad S01 - S03 1080p", new[] { 1, 2, 3 })]
    [InlineData("Breaking Bad S1 - 3 1080p", new[] { 1, 2, 3 })]
    [InlineData("The Legend of Korra Season 02 - 04 (720p) x264", new[] { 2, 3, 4 })]
    [InlineData("Show Seasons 1-3 1080p", new[] { 1, 2, 3 })]
    public void SeasonRanges_StillCoverEverySeason(string name, int[] seasons)
    {
        TorrentCandidateParser.Parse(name).CoveredSeasons.Should().Equal(seasons);
    }

    [Fact]
    public void EpisodeEvaluator_MatchesSeasonDashEpisodeByAlternativeTitle()
    {
        var show = TrackedShowBuilder.Show("Ascendance of a Bookworm", year: 2019, alternativeTitles: "Honzuki no Gekokujou");
        var episode = TrackedShowBuilder.Episode(4, 15);
        var recipe = RecipeBuilder.TvEpisode().Build();
        var evaluator = new CandidateEvaluationService(new SearchTitleResolver());

        var accepted = evaluator.EvaluateEpisode(recipe, show, episode, TorrentResultBuilder.Magnet("[SubsPlease] Honzuki no Gekokujou S4 - 15 (1080p) [B5E7B82B].mkv"));
        var otherEpisode = evaluator.EvaluateEpisode(recipe, show, episode, TorrentResultBuilder.Magnet("[SubsPlease] Honzuki no Gekokujou S4 - 17 (1080p) [E56B1187].mkv"));
        var otherShow = evaluator.EvaluateEpisode(
            RecipeBuilder.TvEpisode().Build(),
            TrackedShowBuilder.Show("Hunter x Hunter", year: 2011),
            TrackedShowBuilder.Episode(1, 5),
            TorrentResultBuilder.Magnet("Monster Hunter Stories S01E05 1080p WEB-DL"));

        accepted.IsAccepted.Should().BeTrue(accepted.RejectDetail);
        otherEpisode.RejectReason.Should().Be(CandidateRejectReason.EpisodeMismatch);
        otherShow.RejectReason.Should().Be(CandidateRejectReason.TitleMismatch);
    }

    [Fact]
    public void RepeatedWords_CountOnlyAsOftenAsTheReleaseRepeatsThem()
    {
        var hunter = TitleNormalizer.SignificantTitleTokens("Hunter x Hunter", distinct: false);

        TitleNormalizer.CountTitleTokenMatches(hunter, ["Monster", "Hunter", "Stories"]).Should().Be(1);
        TitleNormalizer.CountTitleTokenMatches(hunter, ["Hunter", "x", "Hunter"]).Should().Be(2);
        TitleNormalizer.CountTitleTokenMatches(TitleNormalizer.SignificantTitleTokens("Schindler's List", distinct: false), ["Schindler", "s", "List"]).Should().Be(2);
    }

    [Fact]
    public void Tokenize_SplitsWordsGluedToBrackets()
    {
        TorrentCandidateParser.Tokenize("[Ex-torrenty.org]BoJack.Horseman.S06").Should().Equal("Ex", "torrenty", "org", "BoJack", "Horseman", "S06");
    }

    private static (CandidateRejectReason Reason, string Detail) Evaluate(string title, string alternative, string name, int season)
    {
        var show = string.IsNullOrEmpty(alternative)
            ? TrackedShowBuilder.Show(title, year: null)
            : TrackedShowBuilder.Show(title, year: null, alternativeTitles: alternative);
        var recipe = RecipeBuilder.TvSeasonPack().Build();
        var result = TorrentResultBuilder.Magnet(name);
        return SeasonPackIdentity.GetRejectReason(show, [season], result, TorrentCandidateParser.Parse(result.FileName), recipe);
    }
}
