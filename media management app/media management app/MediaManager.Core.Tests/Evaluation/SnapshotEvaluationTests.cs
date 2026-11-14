using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Evaluation;

/// <summary>
/// A show snapshot serves several wanted episodes. To score it for the search compare log the row is judged against
/// each target and the best verdict wins: a row is "good" when it is good for at least one wanted episode.
/// </summary>
public sealed class SnapshotEvaluationTests
{
    private static readonly SearchRecipe Recipe = new() { Name = "TV", TargetKind = MediaKind.TvEpisode };

    private static TorrentSearchResult Row(string name, int seeders = 20) =>
        new() { FileName = name, FileUrl = $"magnet:?xt=urn:btih:{name.GetHashCode():x8}", Seeders = seeders };

    private static RecipeCandidateResult Accepted(TorrentSearchResult row, int score) =>
        new() { SearchResult = row, TotalScore = score };

    private static RecipeCandidateResult Rejected(TorrentSearchResult row) =>
        new() { SearchResult = row, RejectReason = CandidateRejectReason.EpisodeMismatch, RejectDetail = "other episode" };

    [Fact]
    public void RowAcceptedForOneTarget_IsAccepted_EvenWhenAnotherTargetRejectsIt()
    {
        var row = Row("Show.01.S01E02.1080p");
        var combined = SnapshotEvaluation.BestOf(Recipe,
        [
            r => Rejected(r),
            r => Accepted(r, 50)
        ]);

        var result = combined(row);

        Assert.True(result.IsAccepted);
        Assert.Equal(50, result.TotalScore);
    }

    [Fact]
    public void RowRejectedByEveryTarget_IsRejected()
    {
        var combined = SnapshotEvaluation.BestOf(Recipe, [r => Rejected(r), r => Rejected(r)]);

        Assert.False(combined(Row("Show.01.S09E09.1080p")).IsAccepted);
    }

    [Fact]
    public void HigherTier_BeatsHigherScore()
    {
        // 1080p with seeders is Good; 720p-or-lower with few seeders is merely Accepted, whatever its score.
        var good = Row("Show.01.S01E02.1080p", seeders: 20);
        var weak = Row("Show.01.S01E03.480p", seeders: 1);
        var combined = SnapshotEvaluation.BestOf(Recipe,
        [
            _ => Accepted(weak, 99),
            _ => Accepted(good, 10)
        ]);

        var result = combined(good);

        Assert.Same(good, result.SearchResult);
        Assert.Equal(CandidateTier.Good, CandidateTiers.Classify(Recipe, result));
    }

    [Fact]
    public void EqualTier_TakesTheHigherScore()
    {
        var row = Row("Show.01.S01E02.1080p");
        var combined = SnapshotEvaluation.BestOf(Recipe, [r => Accepted(r, 10), r => Accepted(r, 70), r => Accepted(r, 30)]);

        Assert.Equal(70, combined(row).TotalScore);
    }

    [Fact]
    public void SameRowIsEvaluatedOncePerTarget_EvenWhenAskedAgain()
    {
        // The compare log scores the native rows and the qBittorrent rows; the same listing must not be judged twice.
        var calls = 0;
        var row = Row("Show.01.S01E02.1080p");
        var combined = SnapshotEvaluation.BestOf(Recipe, [r => { calls++; return Accepted(r, 1); }, r => { calls++; return Accepted(r, 2); }]);

        combined(row);
        combined(row);

        Assert.Equal(2, calls);
    }
}
