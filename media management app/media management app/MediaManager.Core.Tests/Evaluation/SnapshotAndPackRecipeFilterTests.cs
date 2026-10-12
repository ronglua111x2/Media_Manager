using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Evaluation;

public class SnapshotAndPackRecipeFilterTests
{
    [Fact]
    public void SnapshotMatcher_RecipeAllows2160p_IgnoresShowPreferredQuality1080p()
    {
        var match = MatchSnapshot(
            "[Subeteka] Jujutsu Kaisen - S03E01 [2160p WEB DUAL DDP2.0 H.265] [7BE3B471].mkv",
            RecipeBuilder.TvEpisode().QualityAllowList("2160p", "1080p").Build());

        match.IsAccepted.Should().BeTrue();
        match.RejectReasonCode.Should().Be(CandidateRejectReason.None);
    }

    [Fact]
    public void SnapshotMatcher_RecipeRejects720p_EvenWhenShowPreferredQualityIsUnsetForMatching()
    {
        var match = MatchSnapshot(
            "[Subeteka] Jujutsu Kaisen - S03E01 [720p WEB DUAL DDP2.0 H.265] [7BE3B471].mkv",
            RecipeBuilder.TvEpisode().QualityAllowList("2160p", "1080p").Build());

        match.IsAccepted.Should().BeFalse();
        match.RejectReasonCode.Should().Be(CandidateRejectReason.QualityMismatch);
    }

    [Fact]
    public void PackIdentity_RecipeAllows2160p_IgnoresShowPreferredQuality1080p()
    {
        var show = TrackedShowBuilder.Show("Jujutsu Kaisen", year: 2020);
        show.PreferredQuality = "1080p";
        var recipe = RecipeBuilder.TvSeasonPack().QualityAllowList("2160p").Build();
        var result = TorrentResultBuilder.Magnet("Jujutsu Kaisen S03 Complete Batch 2160p WEB-DL");
        var parsed = TorrentCandidateParser.Parse(result.FileName);

        var reject = SeasonPackIdentity.GetRejectReason(show, [3], result, parsed, recipe);

        reject.Reason.Should().Be(CandidateRejectReason.None);
        parsed.CoveredSeasons.Should().Contain(3);
    }

    [Fact]
    public void PackIdentity_RecipeRejects1080pWhenAllowListIs2160p()
    {
        var show = TrackedShowBuilder.Show("Jujutsu Kaisen", year: 2020);
        show.PreferredQuality = "1080p";
        var recipe = RecipeBuilder.TvSeasonPack().QualityAllowList("2160p").Build();
        var result = TorrentResultBuilder.Magnet("Jujutsu Kaisen S03 Complete Batch 1080p WEB-DL");
        var parsed = TorrentCandidateParser.Parse(result.FileName);

        var reject = SeasonPackIdentity.GetRejectReason(show, [3], result, parsed, recipe);

        reject.Reason.Should().Be(CandidateRejectReason.QualityMismatch);
    }

    private static SnapshotMatchResult MatchSnapshot(string fileName, SearchRecipe recipe)
    {
        var show = TrackedShowBuilder.Show("Jujutsu Kaisen", year: 2020);
        show.PreferredQuality = "1080p";
        show.MinimumSeeders = 50;
        var episode = TrackedShowBuilder.Episode(3, 1, title: string.Empty);
        var result = TorrentResultBuilder.Magnet(fileName, seeders: 5);
        var candidate = new SnapshotCandidate
        {
            Result = result,
            Parsed = TorrentCandidateParser.Parse(result.FileName)
        };

        return new SnapshotCandidateMatcher().Match(
            show,
            episode,
            candidate,
            recipe,
            ["Jujutsu Kaisen"],
            CandidateScoringWeights.Default);
    }
}
