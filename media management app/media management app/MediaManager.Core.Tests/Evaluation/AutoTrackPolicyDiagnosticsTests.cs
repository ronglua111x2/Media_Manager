using FluentAssertions;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Evaluation;

public class RecipeOverrideRejectTests
{
    [Fact]
    public void SizeBelowOverride_StockHasNoMin_IsOverrideReject()
    {
        var stock = RecipeBuilder.TvEpisode().Build();
        var overrides = new RecipeExecutionOverrides
        {
            OverrideMinSize = true,
            MinSizeBytes = 600L * 1024 * 1024
        };

        RecipeOverrideReject.IsOverrideReject(
                overrides,
                stock,
                CandidateRejectReason.SizeTooSmall,
                seeders: 2014,
                fileSize: (long)(587.8 * 1024 * 1024))
            .Should().BeTrue();
    }

    [Fact]
    public void SizeBelowStockAndOverride_IsNotOverrideReject()
    {
        var stock = RecipeBuilder.TvEpisode().Build();
        SetMinimumSizeBytes(stock, 700L * 1024 * 1024);
        var overrides = new RecipeExecutionOverrides
        {
            OverrideMinSize = true,
            MinSizeBytes = 800L * 1024 * 1024
        };

        RecipeOverrideReject.IsOverrideReject(
                overrides,
                stock,
                CandidateRejectReason.SizeTooSmall,
                seeders: 100,
                fileSize: 600L * 1024 * 1024)
            .Should().BeFalse();
    }

    [Fact]
    public void SeedersBelowOverride_StockWouldAccept_IsOverrideReject()
    {
        var stock = RecipeBuilder.TvEpisode().MinimumSeeders(0).Build();
        var overrides = new RecipeExecutionOverrides { MinSeeders = 50 };

        RecipeOverrideReject.IsOverrideReject(
                overrides,
                stock,
                CandidateRejectReason.SeedersTooLow,
                seeders: 5,
                fileSize: 800L * 1024 * 1024)
            .Should().BeTrue();
    }

    [Fact]
    public void SeedersBelowStock_IsNotOverrideReject()
    {
        var stock = RecipeBuilder.TvEpisode().MinimumSeeders(50).Build();
        var overrides = new RecipeExecutionOverrides { MinSeeders = 50 };

        RecipeOverrideReject.IsOverrideReject(
                overrides,
                stock,
                CandidateRejectReason.SeedersTooLow,
                seeders: 5,
                fileSize: 800L * 1024 * 1024)
            .Should().BeFalse();
    }

    [Fact]
    public void QualityMismatch_IsNotOverrideReject()
    {
        var stock = RecipeBuilder.TvEpisode().Build();
        var overrides = new RecipeExecutionOverrides { MinSeeders = 10 };

        RecipeOverrideReject.IsOverrideReject(
                overrides,
                stock,
                CandidateRejectReason.QualityMismatch,
                seeders: 100,
                fileSize: 800L * 1024 * 1024)
            .Should().BeFalse();
    }

    [Fact]
    public void CountOverrideRejects_CountsOnlyOverrideKills()
    {
        var stock = RecipeBuilder.TvEpisode().MinimumSeeders(0).Build();
        var overrides = new RecipeExecutionOverrides
        {
            OverrideMinSize = true,
            MinSizeBytes = 600L * 1024 * 1024
        };
        var evaluated = new List<RecipeCandidateResult>
        {
            new()
            {
                SearchResult = TorrentResultBuilder.Magnet("Show S01E01 1080p", seeders: 20, fileSize: 587L * 1024 * 1024),
                RejectReason = CandidateRejectReason.SizeTooSmall
            },
            new()
            {
                SearchResult = TorrentResultBuilder.Magnet("Show S01E01 720p", seeders: 20, fileSize: 800L * 1024 * 1024),
                RejectReason = CandidateRejectReason.QualityMismatch
            }
        };

        var counts = RecipeOverrideReject.CountOverrideRejects(evaluated, overrides, stock);

        counts.Should().ContainKey(CandidateRejectReason.SizeTooSmall);
        counts[CandidateRejectReason.SizeTooSmall].Should().Be(1);
        counts.Should().NotContainKey(CandidateRejectReason.QualityMismatch);
    }

    private static void SetMinimumSizeBytes(SearchRecipe recipe, long bytes)
    {
        var filter = recipe.Modules.First(module => module.BlockType == RecipeBlockType.CandidateFilter);
        filter.MinimumSizeBytes = bytes;
    }
}

public class HuntOverrideDebugFormatterTests
{
    [Fact]
    public void FormatOverridesFlag_OnAndOff()
    {
        HuntOverrideDebugFormatter.FormatOverridesFlag(true).Should().Be("on");
        HuntOverrideDebugFormatter.FormatOverridesFlag(false).Should().Be("off");
    }

    [Fact]
    public void FormatRejectOverrideSuffix_OnlyWhenOverride()
    {
        HuntOverrideDebugFormatter.FormatRejectOverrideSuffix(true).Should().Be(" by=override");
        HuntOverrideDebugFormatter.FormatRejectOverrideSuffix(false).Should().BeEmpty();
    }

    [Fact]
    public void FormatHuntOverrideSummary_IncludesCounts()
    {
        var line = HuntOverrideDebugFormatter.FormatHuntOverrideSummary(
            matchedWithoutOverride: 5,
            kept: 0,
            new Dictionary<CandidateRejectReason, int> { [CandidateRejectReason.SizeTooSmall] = 5 });

        line.Should().Be("HuntOverride RecipeMatchedWithoutOverride=5 Kept=0 Rejects=SizeTooSmall×5");
    }

    [Fact]
    public void FormatHuntOverrideSummary_Empty_ReturnsNull()
    {
        HuntOverrideDebugFormatter.FormatHuntOverrideSummary(0, 0, new Dictionary<CandidateRejectReason, int>())
            .Should().BeNull();
    }
}

public class HuntLogFormatterTests
{
    [Fact]
    public void FormatEpisodeLine_OverrideFailure_IncludesRejectedByOverride()
    {
        var outcome = new HuntEpisodeOutcome
        {
            ShowTitle = "President Curtis",
            EpisodeLabel = "S01E05 David",
            SearchRows = 197,
            RecipeMatched = 5,
            PolicyKept = 0,
            Stage = HuntEpisodeStage.RecipeMatch,
            FailureReason = HuntLogFormatter.FormatOverrideRejectFailure(
                new Dictionary<CandidateRejectReason, int> { [CandidateRejectReason.SizeTooSmall] = 5 })
        };

        var line = HuntLogFormatter.FormatEpisodeLine(outcome);

        line.Should().StartWith("[HUNT] President Curtis S01E05 David: FAILED (recipe)");
        line.Should().Contain("search 197, matched 5, kept 0");
        line.Should().Contain("Rejected by override. SizeTooSmall×5");
        line.Should().NotContain("FAILED (policy)");
        line.Should().NotContain("MinFileSizeMb=");
    }

    [Fact]
    public void FormatEpisodeLine_Added_UsesAddedLabel()
    {
        var outcome = new HuntEpisodeOutcome
        {
            ShowTitle = "Some Show",
            EpisodeLabel = "S02E03",
            SearchRows = 40,
            RecipeMatched = 3,
            PolicyKept = 2,
            Stage = HuntEpisodeStage.Succeeded,
            AddedCandidateName = "Some.Show.S02E03.1080p"
        };

        HuntLogFormatter.FormatEpisodeLine(outcome)
            .Should().Be("[HUNT] Some Show S02E03: ADDED — search 40, matched 3, kept 2 → Some.Show.S02E03.1080p");
    }

    [Fact]
    public void FormatHumanSummary_AppendsCompactFailures()
    {
        var outcomes = new List<HuntEpisodeOutcome>
        {
            new()
            {
                ShowTitle = "President Curtis",
                EpisodeLabel = "S01E05",
                Stage = HuntEpisodeStage.RecipeMatch,
                FailureReason = "Rejected by override. SizeTooSmall×5"
            }
        };

        var summary = HuntLogFormatter.FormatHumanSummary(
            "Hunt: shows=1, queued=1, candidates=0, added=0, failed=1.",
            outcomes);

        summary.Should().Contain("Hunt: shows=1");
        summary.Should().Contain("President Curtis S01E05: Rejected by override. SizeTooSmall×5");
    }

    [Fact]
    public void FormatEndedBy_TimeoutIncludesCollectedRows()
    {
        HuntLogFormatter.FormatEndedBy("timeout", 197)
            .Should().Be("timeout (collected 197 rows)");
    }
}
