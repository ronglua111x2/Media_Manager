using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Evaluation;

public class AutoTrackPolicyDiagnosticsTests
{
    [Fact]
    public void SizeBelowMin_RejectsSizeTooSmall()
    {
        var reason = AutoTrackPolicyDiagnostics.GetRejectReason(
            minSeeders: 10,
            minQuality: "1080p",
            allowedQualities: null,
            minFileSizeMb: 600,
            maxFileSizeMb: null,
            seeders: 2014,
            qualityLabel: "1080p",
            fileName: "President Curtis S01E05 1080p WEB h264-EDITH",
            fileSize: (long)(587.8 * 1024 * 1024));

        reason.Should().Be(CandidateRejectReason.SizeTooSmall);
    }

    [Fact]
    public void SeedersBelowMin_RejectsSeedersTooLow()
    {
        var reason = AutoTrackPolicyDiagnostics.GetRejectReason(
            minSeeders: 50,
            minQuality: "1080p",
            allowedQualities: null,
            minFileSizeMb: null,
            maxFileSizeMb: null,
            seeders: 5,
            qualityLabel: "1080p",
            fileName: "Show S01E01 1080p",
            fileSize: 800L * 1024 * 1024);

        reason.Should().Be(CandidateRejectReason.SeedersTooLow);
    }

    [Fact]
    public void QualityBelowMin_RejectsQualityMismatch()
    {
        var reason = AutoTrackPolicyDiagnostics.GetRejectReason(
            minSeeders: 0,
            minQuality: "1080p",
            allowedQualities: null,
            minFileSizeMb: null,
            maxFileSizeMb: null,
            seeders: 100,
            qualityLabel: "720p",
            fileName: "Show S01E01 720p",
            fileSize: 800L * 1024 * 1024);

        reason.Should().Be(CandidateRejectReason.QualityMismatch);
    }

    [Fact]
    public void MatchingRelease_ReturnsNull()
    {
        var reason = AutoTrackPolicyDiagnostics.GetRejectReason(
            minSeeders: 10,
            minQuality: "1080p",
            allowedQualities: null,
            minFileSizeMb: 500,
            maxFileSizeMb: null,
            seeders: 2014,
            qualityLabel: "1080p",
            fileName: "President Curtis S01E05 1080p WEB h264-EDITH",
            fileSize: 700L * 1024 * 1024);

        reason.Should().BeNull();
    }
}

public class HuntLogFormatterTests
{
    [Fact]
    public void FormatEpisodeLine_PolicyFailure_IncludesCountsAndMinSize()
    {
        var outcome = new HuntEpisodeOutcome
        {
            ShowTitle = "President Curtis",
            EpisodeLabel = "S01E05 David",
            SearchRows = 197,
            RecipeMatched = 5,
            PolicyKept = 0,
            Stage = HuntEpisodeStage.AutoTrackPolicy,
            FailureReason = "SizeTooSmall×5",
            PolicyRejectCounts = { [CandidateRejectReason.SizeTooSmall] = 5 },
            PolicyMinFileSizeMb = 600,
            BestRejectedFileSize = (long)(587.8 * 1024 * 1024)
        };

        var line = HuntLogFormatter.FormatEpisodeLine(outcome);

        line.Should().StartWith("[HUNT] President Curtis S01E05 David: FAILED (policy)");
        line.Should().Contain("search 197, matched 5, kept 0");
        line.Should().Contain("SizeTooSmall×5");
        line.Should().Contain("MinFileSizeMb=600");
        line.Should().Contain("best=587.8 MiB");
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
                Stage = HuntEpisodeStage.AutoTrackPolicy,
                FailureReason = "SizeTooSmall×5"
            }
        };

        var summary = HuntLogFormatter.FormatHumanSummary(
            "Hunt: shows=1, queued=1, candidates=0, added=0, failed=1.",
            outcomes);

        summary.Should().Contain("Hunt: shows=1");
        summary.Should().Contain("President Curtis S01E05: SizeTooSmall×5");
    }

    [Fact]
    public void FormatEndedBy_TimeoutIncludesCollectedRows()
    {
        HuntLogFormatter.FormatEndedBy("timeout", 197)
            .Should().Be("timeout (collected 197 rows)");
    }
}
