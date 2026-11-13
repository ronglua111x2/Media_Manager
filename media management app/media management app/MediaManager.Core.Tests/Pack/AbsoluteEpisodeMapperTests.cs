using FluentAssertions;
using media_management_app.Common;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Pack;

public class AbsoluteEpisodeMapperTests
{
    private static readonly AbsoluteEpisodeMapper Mapper = new(new Dictionary<int, int> { [0] = 5, [1] = 12, [2] = 12, [3] = 24 });

    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(12, 1, 1, 12)]
    [InlineData(13, 1, 2, 1)]
    [InlineData(15, 1, 2, 3)]
    [InlineData(24, 2, 2, 12)]
    [InlineData(25, 3, 3, 1)]
    [InlineData(48, 3, 3, 24)]
    public void TryMap_CountsThroughSeasonsWithoutSpecials(int absolute, int groupSeason, int season, int episode)
    {
        Mapper.TryMap(absolute, groupSeason, Episodes(groupSeason == 3 ? 24 : 12), out var mappedSeason, out var mappedEpisode, out _)
            .Should().BeTrue();

        (mappedSeason, mappedEpisode).Should().Be((season, episode));
    }

    [Fact]
    public void TryMap_NumberAlreadyInGroupSeason_StaysInThatSeason()
    {
        Mapper.TryMap(5, 2, Episodes(12), out var season, out var episode, out _).Should().BeTrue();

        (season, episode).Should().Be((2, 5));
    }

    [Fact]
    public void TryMap_ContinuousTmdbNumbering_KeepsDirectNumber()
    {
        var mapper = new AbsoluteEpisodeMapper(new Dictionary<int, int> { [21] = 10, [22] = 9 });

        mapper.TryMap(1157, 22, new HashSet<int>(Enumerable.Range(1154, 9)), out var season, out var episode, out _)
            .Should().BeTrue();

        (season, episode).Should().Be((22, 1157));
    }

    [Fact]
    public void TryMap_BeyondShow_FailsWithReason()
    {
        Mapper.TryMap(49, 1, Episodes(12), out _, out _, out var reason).Should().BeFalse();

        reason.Should().Contain("beyond");
    }

    [Fact]
    public void NaturalStringComparer_OrdersDigitRunsByValue()
    {
        var names = new[] { "Show - 10", "Show - 2", "Show - 1", "Show - 02", "show - 3" };

        names.Order(NaturalStringComparer.OrdinalIgnoreCase).Should()
            .Equal("Show - 1", "Show - 02", "Show - 2", "show - 3", "Show - 10");
    }

    private static HashSet<int> Episodes(int count) => Enumerable.Range(1, count).ToHashSet();
}
