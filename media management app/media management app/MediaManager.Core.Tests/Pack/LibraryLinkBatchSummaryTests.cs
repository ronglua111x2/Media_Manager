using FluentAssertions;
using media_management_app.Common;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Pack;

public class LibraryLinkBatchSummaryTests
{
    [Fact]
    public void FormatSymlinkCreated_IncludesRegularMatchedAndOrphanCounts()
    {
        LibraryLinkBatchSummary.FormatSymlinkCreated("Korra", 52, 1, 1)
            .Should().Be("Korra — 52 episodes, 1 matched special, 1 extra symlinked");
    }

    [Fact]
    public void FormatSymlinkCreated_OmitsZeroCountsAndPluralizes()
    {
        LibraryLinkBatchSummary.FormatSymlinkCreated("BoJack", 1, 0, 0)
            .Should().Be("BoJack — 1 episode symlinked");
        LibraryLinkBatchSummary.FormatSymlinkCreated("BoJack", 0, 2, 3)
            .Should().Be("BoJack — 2 matched specials, 3 extras symlinked");
    }

    [Fact]
    public void FormatSymlinkCreated_FallsBackWhenNoClassifiedMembers()
    {
        LibraryLinkBatchSummary.FormatSymlinkCreated(" ", 0, 0, 0)
            .Should().Be("Unknown Show — items symlinked");
    }

    [Fact]
    public void CountMembers_ClassifiesRegularS00AndOrphans()
    {
        var members = new (bool IsOrphanPackSpecial, int? MappedSeasonNumber, int? SeasonNumber)[]
        {
            (false, 1, 1),
            (false, 2, 2),
            (false, AppConstants.SpecialsSeasonNumber, AppConstants.SpecialsSeasonNumber),
            (true, AppConstants.SpecialsSeasonNumber, AppConstants.SpecialsSeasonNumber),
            (false, null, null)
        };

        var counts = LibraryLinkBatchSummary.CountMembers(members);

        counts.RegularEpisodes.Should().Be(2);
        counts.MatchedSpecials.Should().Be(1);
        counts.OrphanExtras.Should().Be(1);
    }

    [Theory]
    [InlineData(false, 4, 4, PackLinkMemberKind.RegularEpisode)]
    [InlineData(false, 0, 0, PackLinkMemberKind.MatchedSpecial)]
    [InlineData(true, 0, 0, PackLinkMemberKind.OrphanExtra)]
    [InlineData(false, null, null, PackLinkMemberKind.Other)]
    public void Classify_MapsPackMemberKinds(
        bool isOrphan,
        int? mappedSeason,
        int? season,
        PackLinkMemberKind expected)
    {
        PackLinkMemberClassifier.Classify(isOrphan, mappedSeason, season).Should().Be(expected);
    }
}
