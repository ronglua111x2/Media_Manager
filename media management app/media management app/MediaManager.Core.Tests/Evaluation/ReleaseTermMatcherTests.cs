using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Evaluation;

public class ReleaseTermMatcherTests
{
    private const string ElCamino = "El.Camino.A.Breaking.Bad.Movie.2019.1080p.NF.WEB-DL.DDP5.1.H264-CMRG";

    [Theory]
    [InlineData("Movie.2019.CAM.x264", "CAM")]
    [InlineData("Movie 2019 [CAM] x264", "CAM")]
    [InlineData("CAM Movie 2019", "CAM")]
    [InlineData("Movie 2019 CAM", "CAM")]
    [InlineData("Movie_2019_CAM_x264", "CAM")]
    [InlineData("Movie.2019.TS.x264", "TS")]
    [InlineData("[Erai-raws] Show - 07 [RAW]", "RAW")]
    [InlineData("Movie.2019.BluRay.DTS-HD.MA.5.1", "DTS")]
    [InlineData("Movie.2019.TrueHD.7.1.Atmos", "TrueHD.7.1")]
    [InlineData("Movie.2019.1080p.WEB-DL-NO ADS", "NO ADS")]
    [InlineData("Movie.2019.1080p-GRP", "-GRP")]
    [InlineData("Movie.2019.2160p.DV.HDR", "  DV  ")]
    public void Matches_WholeToken_SameCase(string name, string term)
    {
        ReleaseTermMatcher.Matches(name, term).Should().BeTrue();
    }

    [Theory]
    [InlineData(ElCamino, "CAM")]
    [InlineData("Movie.2019.HDCAM.x264", "CAM")]
    [InlineData("Movie.2019.CAMRip.x264", "CAM")]
    [InlineData("Movie.2019.cam.x264", "CAM")]
    [InlineData("Movie.2019.BluRay.DTS-HD.MA.5.1", "TS")]
    [InlineData("Its.Always.Sunny.S01E01", "TS")]
    [InlineData("Show.S01E01.Drawn.Together", "Raw")]
    [InlineData("[Erai-raws] Show - 07 [RAW]", "Raw")]
    [InlineData("Movie.2019.DVDRip.x264", "DV")]
    [InlineData("Adventure.Time.S01E01", "DV")]
    [InlineData("Movie.2019.1080p", "")]
    [InlineData("", "CAM")]
    public void DoesNotMatch_PartialWordOrDifferentCase(string name, string term)
    {
        ReleaseTermMatcher.Matches(name, term).Should().BeFalse();
    }

    [Fact]
    public void PreferredTermMatcher_CountsWholeTokensOnly()
    {
        PreferredTermMatcher.CountMatches("Movie.2019.2160p.DV.HDR.Dolby.Atmos", "Dolby, DV").Should().Be(2);
        PreferredTermMatcher.CountMatches("Movie.2019.DVDRip.DOLBY", "Dolby, DV").Should().Be(0);
    }

    [Fact]
    public void PreferredTermMatcher_KeepsTermsThatDifferOnlyByCase()
    {
        PreferredTermMatcher.SplitTerms("RAW, Raw").Should().Equal("RAW", "Raw");
        PreferredTermMatcher.CountMatches("[Group] Show - 01 [RAW]", new[] { "RAW", "Raw" }).Should().Be(1);
    }

    [Fact]
    public void EvaluateMovie_TitleContainingExcludedTermPrefix_IsNotExcluded()
    {
        var titles = new FakeSearchTitleResolver { Titles = ["El Camino A Breaking Bad Movie"] };
        var sut = new CandidateEvaluationService(titles);
        var recipe = RecipeBuilder.Movie().ExcludeTerms("CAM", "TS").Build();
        var movie = TrackedShowBuilder.Movie("El Camino A Breaking Bad Movie", 2019);

        var result = sut.EvaluateMovie(recipe, movie, TorrentResultBuilder.Magnet(ElCamino));

        result.RejectReason.Should().NotBe(CandidateRejectReason.ExcludedTerm);
    }

    [Fact]
    public void EvaluateMovie_ExcludedTermAsToken_Rejects()
    {
        var titles = new FakeSearchTitleResolver { Titles = ["El Camino A Breaking Bad Movie"] };
        var sut = new CandidateEvaluationService(titles);
        var recipe = RecipeBuilder.Movie().ExcludeTerms("CAM").Build();
        var movie = TrackedShowBuilder.Movie("El Camino A Breaking Bad Movie", 2019);

        var result = sut.EvaluateMovie(
            recipe,
            movie,
            TorrentResultBuilder.Magnet("El.Camino.A.Breaking.Bad.Movie.2019.1080p.CAM.x264"));

        result.RejectReason.Should().Be(CandidateRejectReason.ExcludedTerm);
    }

    [Fact]
    public void EvaluateMovie_BlockedGroupMatchesWholeTokenOnly()
    {
        var titles = new FakeSearchTitleResolver { Titles = ["El Camino A Breaking Bad Movie"] };
        var sut = new CandidateEvaluationService(titles);
        var movie = TrackedShowBuilder.Movie("El Camino A Breaking Bad Movie", 2019);

        var partial = sut.EvaluateMovie(
            RecipeBuilder.Movie().BlockedGroups("CMR").Build(),
            movie,
            TorrentResultBuilder.Magnet(ElCamino));
        var whole = sut.EvaluateMovie(
            RecipeBuilder.Movie().BlockedGroups("CMRG").Build(),
            movie,
            TorrentResultBuilder.Magnet(ElCamino));

        partial.RejectReason.Should().NotBe(CandidateRejectReason.BlockedReleaseGroup);
        whole.RejectReason.Should().Be(CandidateRejectReason.BlockedReleaseGroup);
    }
}
