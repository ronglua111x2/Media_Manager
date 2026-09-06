using FluentAssertions;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Ratings;

public class HeatmapStripLayoutTests
{
    [Fact]
    public void FitCount_UsesCurrentStorySlotWidth()
    {
        HeatmapStripLayout.FitCount(620, HeatmapStripLayout.StorySlotWidth).Should().Be(10);
        HeatmapStripLayout.FitCount(619, HeatmapStripLayout.StorySlotWidth).Should().Be(9);
        HeatmapStripLayout.FitCount(HeatmapStripLayout.StorySlotWidth, HeatmapStripLayout.StorySlotWidth).Should().Be(1);
        HeatmapStripLayout.FitCount(HeatmapStripLayout.StorySlotWidth - 1, HeatmapStripLayout.StorySlotWidth).Should().Be(0);
        HeatmapStripLayout.FitCount(0, HeatmapStripLayout.StorySlotWidth).Should().Be(0);
    }

    [Fact]
    public void NeedsExpand_WhenMoreSeasonsOrPreviewOverflows()
    {
        HeatmapStripLayout.NeedsExpand(seasonGroupCount: 1, previewCellCount: 10, fitCount: 10).Should().BeFalse();
        HeatmapStripLayout.NeedsExpand(seasonGroupCount: 1, previewCellCount: 11, fitCount: 10).Should().BeTrue();
        HeatmapStripLayout.NeedsExpand(seasonGroupCount: 2, previewCellCount: 5, fitCount: 10).Should().BeTrue();
        HeatmapStripLayout.NeedsExpand(seasonGroupCount: 1, previewCellCount: 0, fitCount: 10).Should().BeFalse();
    }

    [Fact]
    public void NextBatchCount_CapsAtVisualBatchSize()
    {
        HeatmapStripLayout.NextBatchCount(0).Should().Be(0);
        HeatmapStripLayout.NextBatchCount(12).Should().Be(12);
        HeatmapStripLayout.NextBatchCount(48).Should().Be(48);
        HeatmapStripLayout.NextBatchCount(49).Should().Be(48);
        HeatmapStripLayout.NextBatchCount(100, batchSize: 10).Should().Be(10);
        HeatmapStripLayout.NextBatchCount(5, batchSize: 10).Should().Be(5);
    }

    [Fact]
    public void Fingerprint_ChangesWhenEpisodeCountsChange()
    {
        var first = new ShowHeatmapRow
        {
            ShowId = 1,
            EpisodeCount = 12,
            RatedEpisodeCount = 4,
            Seasons =
            [
                new HeatmapSeasonGroup
                {
                    SeasonNumber = 1,
                    Cells = [Cell(1), Cell(2)]
                }
            ]
        };
        var sameShape = new ShowHeatmapRow
        {
            ShowId = 1,
            EpisodeCount = 12,
            RatedEpisodeCount = 4,
            Seasons =
            [
                new HeatmapSeasonGroup
                {
                    SeasonNumber = 1,
                    Cells = [Cell(1), Cell(2)]
                }
            ]
        };
        var extraEpisode = new ShowHeatmapRow
        {
            ShowId = 1,
            EpisodeCount = 13,
            RatedEpisodeCount = 4,
            Seasons =
            [
                new HeatmapSeasonGroup
                {
                    SeasonNumber = 1,
                    Cells = [Cell(1), Cell(2), Cell(3)]
                }
            ]
        };

        HeatmapStripLayout.Fingerprint([first]).Should().Be(HeatmapStripLayout.Fingerprint([sameShape]));
        HeatmapStripLayout.Fingerprint([first]).Should().NotBe(HeatmapStripLayout.Fingerprint([extraEpisode]));
        HeatmapStripLayout.Fingerprint(Array.Empty<ShowHeatmapRow>()).Should().BeEmpty();
    }

    [Fact]
    public void PosterFitCount_IsThreeRowsOfSlots()
    {
        HeatmapStripLayout.PosterFitCount(0).Should().Be(0);
        HeatmapStripLayout.PosterFitCount(101).Should().Be(0);
        HeatmapStripLayout.PosterFitCount(102).Should().Be(3);
        HeatmapStripLayout.PosterFitCount(1020).Should().Be(30);
        HeatmapStripLayout.NextBatchCount(20, HeatmapStripLayout.PosterVisualBatchSize).Should().Be(8);
        HeatmapStripLayout.NextBatchCount(5, HeatmapStripLayout.PosterVisualBatchSize).Should().Be(5);
    }

    [Fact]
    public void TitleStripFingerprint_ChangesWhenIdsOrRatingsChange()
    {
        var first = new TitleRatingCard { MediaKind = MediaKind.Movie, MediaId = 1, Rating = 8.0 };
        var same = new TitleRatingCard { MediaKind = MediaKind.Movie, MediaId = 1, Rating = 8.0 };
        var differentRating = new TitleRatingCard { MediaKind = MediaKind.Movie, MediaId = 1, Rating = 7.0 };

        HeatmapStripLayout.TitleStripFingerprint([first]).Should().Be(HeatmapStripLayout.TitleStripFingerprint([same]));
        HeatmapStripLayout.TitleStripFingerprint([first]).Should().NotBe(HeatmapStripLayout.TitleStripFingerprint([differentRating]));
        HeatmapStripLayout.TitleStripFingerprint(Array.Empty<TitleRatingCard>()).Should().BeEmpty();
    }

    private static HeatmapEpisodeCell Cell(int episodeNumber) =>
        new() { ShowId = 1, SeasonNumber = 1, EpisodeNumber = episodeNumber };
}
