using FluentAssertions;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Torrent;

/// <summary>
/// Cross-review 2, F2: "Season N Episode M" names one episode. Narrowing NxN to \d{1,2}x\d{1,3} stopped
/// "(640x360)" from accidentally marking such names as episodes, and they became season packs.
/// </summary>
public class SeasonEpisodeWordsTests
{
    [Theory]
    [InlineData("Rick and Morty Season 9 Episode 4 Sneak Peek 'A Ricker Runs Through It' (640x360).mp4", 9, 4)]
    [InlineData("Show Season 7 Episode 9 Full Episode HD (640x360).mp4", 7, 9)]
    [InlineData("Show.Season.2.Ep.13.1080p.WEB.x264", 2, 13)]
    public void SeasonEpisodeWords_AreOneEpisode(string name, int season, int episode)
    {
        var parsed = TorrentCandidateParser.Parse(name);

        parsed.SeasonNumber.Should().Be(season);
        parsed.EpisodeNumber.Should().Be(episode);
        TorrentReleaseKind.Classify(name, parsed).IsEpisode.Should().BeTrue();
        TorrentReleaseKind.Classify(name, parsed).IsPack.Should().BeFalse();
    }

    [Theory]
    [InlineData("Show Season 1 Episode 1-12 1080p BluRay")]
    [InlineData("Show Season 2 Episodes 1-10 Complete 1080p")]
    public void EpisodeRanges_AreNotOneEpisode(string name)
    {
        TorrentReleaseKind.Classify(name).IsEpisode.Should().BeFalse();
    }

    [Fact]
    public void SeasonPack_RejectsASingleSeasonEpisodeName_EpisodeSearchAcceptsIt()
    {
        var show = TrackedShowBuilder.Show("Show", year: 2013);
        const string name = "Show Season 7 Episode 9 1080p WEB x264-GROUP";
        var row = new TorrentSearchResult { FileName = name, FileUrl = "magnet:?xt=urn:btih:bd9c339c8105439135b8938633db632b4df51fbb", FileSize = 900_000_000, Seeders = 40 };
        var packRecipe = RecipeBuilder.TvEpisode().Build();
        packRecipe.TargetKind = MediaKind.TvSeasonPack;

        SeasonPackIdentity.GetRejectReason(show, [7], row, TorrentCandidateParser.Parse(name), packRecipe).Reason
            .Should().Be(CandidateRejectReason.WrongReleaseKind);
        new CandidateEvaluationService(new SearchTitleResolver())
            .EvaluateEpisode(RecipeBuilder.TvEpisode().Build(), show, TrackedShowBuilder.Episode(season: 7, episode: 9), row)
            .IsAccepted.Should().BeTrue();
    }
}
