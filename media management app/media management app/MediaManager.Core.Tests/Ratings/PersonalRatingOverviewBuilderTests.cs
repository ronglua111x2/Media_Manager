using FluentAssertions;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Ratings;

public class PersonalRatingOverviewBuilderTests
{
    [Fact]
    public void Means_IgnoreNullRatings()
    {
        var shows = new[]
        {
            Show(1, "A", rating: 8.0),
            Show(2, "B", rating: null)
        };
        var movies = new[]
        {
            Movie(1, "M1", rating: 6.0),
            Movie(2, "M2", rating: null)
        };
        var episodes = new[]
        {
            Episode(1, 1, 1, rating: 9.0),
            Episode(1, 1, 2, rating: null),
            Episode(2, 1, 1, rating: 7.0)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, movies, episodes);

        overview.RatedShowCount.Should().Be(1);
        overview.RatedMovieCount.Should().Be(1);
        overview.RatedEpisodeCount.Should().Be(2);
        overview.MeanShowRating.Should().Be(8.0);
        overview.MeanMovieRating.Should().Be(6.0);
        overview.MeanEpisodeRating.Should().Be(8.0);
        overview.TitleBands.Single(band => band.Band == EpisodeRatingBand.Great).Count.Should().Be(1);
        overview.TitleBands.Single(band => band.Band == EpisodeRatingBand.Average).Count.Should().Be(1);
        overview.EpisodeBands.Single(band => band.Band == EpisodeRatingBand.Masterpiece).Count.Should().Be(1);
        overview.EpisodeBands.Single(band => band.Band == EpisodeRatingBand.Good).Count.Should().Be(1);
    }

    [Fact]
    public void Heatmap_OnlyIncludesShowsWithAtLeastOneEpisodeScore()
    {
        var shows = new[]
        {
            Show(1, "Rated Eps"),
            Show(2, "No Eps")
        };
        var episodes = new[]
        {
            Episode(1, 1, 1, rating: 8.2),
            Episode(1, 1, 2, rating: null),
            Episode(2, 1, 1, rating: null)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, [], episodes);

        overview.HeatmapRows.Should().ContainSingle(row => row.ShowId == 1);
        overview.HeatmapRows.Should().NotContain(row => row.ShowId == 2);
        overview.HeatmapRows[0].EpisodeCount.Should().Be(2);
        overview.HeatmapRows[0].RatedEpisodeCount.Should().Be(1);
    }

    [Fact]
    public void HiddenStorySeasons_AreIncludedInEpisodeStatsAndMainHeatmap()
    {
        var shows = new[] { Show(1, "Hidden S2", rating: 8.0) };
        var episodes = new[]
        {
            Episode(1, 1, 1, rating: 9.0, isHidden: false),
            Episode(1, 2, 1, rating: 2.0, isHidden: true)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, [], episodes);

        overview.RatedEpisodeCount.Should().Be(2);
        overview.EpisodeCount.Should().Be(2);
        overview.MeanEpisodeRating.Should().Be(5.5);
        overview.HeatmapRows.Should().ContainSingle();
        overview.HeatmapRows[0].Seasons.Select(season => season.SeasonNumber)
            .Should().Equal(1, 2);
        overview.HeatmapRows[0].ExtraSeasons.Should().BeEmpty();
        overview.TopEpisodes.Should().ContainSingle(entry => entry.UserRating == 9.0);
        overview.BottomEpisodes.Should().ContainSingle(entry => entry.UserRating == 2.0);
    }

    [Fact]
    public void Specials_GoToExtrasTailAndSeparateHallOfFame()
    {
        var shows = new[] { Show(1, "Specials", rating: 8.0) };
        var episodes = new[]
        {
            Episode(1, 1, 1, rating: 8.0),
            Episode(1, AppConstants.SpecialsSeasonNumber, 1, rating: 9.5, isHidden: true),
            Episode(1, AppConstants.SpecialsSeasonNumber, 2, rating: null, isHidden: true)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, [], episodes);

        overview.RatedEpisodeCount.Should().Be(2);
        overview.EpisodeCount.Should().Be(3);
        overview.MeanEpisodeRating.Should().Be(8.75);
        overview.HeatmapRows.Should().ContainSingle();
        overview.HeatmapRows[0].EpisodeCount.Should().Be(1);
        overview.HeatmapRows[0].RatedEpisodeCount.Should().Be(1);
        overview.HeatmapRows[0].Seasons.Should().ContainSingle(season => season.SeasonNumber == 1);
        overview.HeatmapRows[0].ExtraSeasons.Should().ContainSingle(season => season.Label == "SP");
        overview.HeatmapRows[0].ExtraSeasons[0].Cells.Should().ContainSingle(cell => cell.EpisodeNumber == 1 && cell.UserRating == 9.5);
        overview.TopEpisodes.Should().ContainSingle(entry => entry.UserRating == 8.0);
        overview.BottomEpisodes.Should().BeEmpty();
        overview.TopSpecials.Should().ContainSingle(entry => entry.UserRating == 9.5 && entry.SeasonNumber == 0);
        overview.BottomSpecials.Should().BeEmpty();
    }

    [Fact]
    public void ShowWithOnlyRatedSpecials_StillHasHeatmapRow()
    {
        var shows = new[] { Show(1, "OVA only") };
        var episodes = new[]
        {
            Episode(1, AppConstants.SpecialsSeasonNumber, 1, rating: 7.0)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, [], episodes);

        overview.HeatmapRows.Should().ContainSingle(row => row.ShowId == 1);
        overview.HeatmapRows[0].EpisodeCount.Should().Be(0);
        overview.HeatmapRows[0].RatedEpisodeCount.Should().Be(0);
        overview.HeatmapRows[0].Seasons.Should().BeEmpty();
        overview.HeatmapRows[0].ExtraSeasons.Should().ContainSingle();
        overview.TopEpisodes.Should().BeEmpty();
        overview.TopSpecials.Should().BeEmpty();
        overview.BottomSpecials.Should().BeEmpty();
    }

    [Fact]
    public void Mismatch_RequiresShowRatingAndThreeEpisodeScores()
    {
        var shows = new[]
        {
            Show(1, "Enough", rating: 9.0),
            Show(2, "Thin", rating: 8.0),
            Show(3, "NoShowScore", rating: null)
        };
        var episodes = new[]
        {
            Episode(1, 1, 1, rating: 7.0),
            Episode(1, 1, 2, rating: 7.0),
            Episode(1, 1, 3, rating: 7.0),
            Episode(2, 1, 1, rating: 8.0),
            Episode(2, 1, 2, rating: 8.0),
            Episode(3, 1, 1, rating: 5.0),
            Episode(3, 1, 2, rating: 5.0),
            Episode(3, 1, 3, rating: 5.0)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, [], episodes);

        overview.Mismatches.Should().ContainSingle(item => item.ShowId == 1);
        overview.Mismatches[0].Delta.Should().BeApproximately(2.0, 0.001);
        overview.Mismatches.Should().NotContain(item => item.ShowId == 2);
        overview.Mismatches.Should().NotContain(item => item.ShowId == 3);
    }

    [Fact]
    public void EmptyOpinions_CompletedUnratedAndShowRatedWithoutEpisodes()
    {
        var shows = new[]
        {
            Show(1, "Done", rating: null, watchStatus: UserWatchStatus.Completed),
            Show(2, "Scored", rating: 8.5, watchStatus: UserWatchStatus.Watching)
        };
        var movies = new[]
        {
            Movie(9, "Film", rating: null, watchStatus: UserWatchStatus.Completed)
        };
        var episodes = new[]
        {
            Episode(2, 1, 1, rating: null)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, movies, episodes);

        overview.EmptyOpinions.Select(item => item.Reason).Should().BeEquivalentTo(
            "Completed, no show rating",
            "Show rated, no episode ratings",
            "Completed, no rating");
    }

    [Fact]
    public void EmptyLibrary_HasNoLibrary()
    {
        var overview = PersonalRatingOverviewBuilder.Build([], [], []);

        overview.HasLibrary.Should().BeFalse();
        overview.HeatmapRows.Should().BeEmpty();
        overview.MeanShowRating.Should().BeNull();
        overview.MeanMovieRating.Should().BeNull();
        overview.MeanEpisodeRating.Should().BeNull();
        overview.WatchStatusStats.Should().BeEmpty();
        overview.RatedShows.Should().BeEmpty();
        overview.BillboardHighlights.Should().BeEmpty();
        overview.BillboardRandom.Should().BeEmpty();
    }

    [Fact]
    public void RatedShows_IncludeTitleRatedShowsMissingFromHeatmap()
    {
        var shows = new[]
        {
            Show(1, "Scored Eps", rating: 8.0),
            Show(2, "Title Only", rating: 9.0)
        };
        var episodes = new[]
        {
            Episode(1, 1, 1, rating: 7.0),
            Episode(2, 1, 1, rating: null)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, [], episodes);

        overview.RatedShows.Select(card => card.MediaId).Should().Equal(2L, 1L);
        overview.HeatmapRows.Should().ContainSingle(row => row.ShowId == 1);
        overview.HeatmapRows.Should().NotContain(row => row.ShowId == 2);
        overview.UnratedShowCount.Should().Be(0);
    }

    [Fact]
    public void RatedTitles_CarryWatchStatus()
    {
        var shows = new[]
        {
            Show(1, "Show", rating: 8.0, watchStatus: UserWatchStatus.Watching)
        };
        var movies = new[]
        {
            Movie(1, "Film", rating: 7.0, watchStatus: UserWatchStatus.Completed)
        };
        var episodes = new[]
        {
            Episode(1, 1, 1, rating: 8.5)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, movies, episodes);

        overview.RatedShows.Should().ContainSingle(card =>
            card.WatchStatus == UserWatchStatus.Watching && card.HasWatchStatus);
        overview.RatedMovies.Should().ContainSingle(card =>
            card.WatchStatus == UserWatchStatus.Completed && card.HasWatchStatus);
        overview.HeatmapRows.Should().ContainSingle(row =>
            row.WatchStatus == UserWatchStatus.Watching && row.HasWatchStatus);
    }

    [Fact]
    public void WatchStatusStats_SplitShowsAndMovies()
    {
        var shows = new[]
        {
            Show(1, "A", watchStatus: UserWatchStatus.Watching),
            Show(2, "B", watchStatus: UserWatchStatus.Watching),
            Show(3, "C", watchStatus: UserWatchStatus.Completed)
        };
        var movies = new[]
        {
            Movie(1, "M1", watchStatus: UserWatchStatus.Completed),
            Movie(2, "M2", watchStatus: UserWatchStatus.None)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, movies, []);

        overview.WatchStatusStats.Should().HaveCount(2);
        var watching = overview.WatchStatusStats.Single(stat => stat.Status == UserWatchStatus.Watching);
        watching.ShowCount.Should().Be(2);
        watching.MovieCount.Should().Be(0);
        watching.SplitText.Should().Be("2 shows · 0 movies");
        var completed = overview.WatchStatusStats.Single(stat => stat.Status == UserWatchStatus.Completed);
        completed.ShowCount.Should().Be(1);
        completed.MovieCount.Should().Be(1);
        overview.WatchStatusStats.Should().NotContain(stat => stat.Status == UserWatchStatus.PlanToWatch);
    }

    [Fact]
    public void Billboard_MixesTitlesAndStoryEpisodes_ExcludesSpecials()
    {
        var shows = new[]
        {
            Show(1, "Alpha", rating: 9.5, thought: "Best show"),
            Show(2, "Beta")
        };
        var movies = new[]
        {
            Movie(1, "Film", rating: 8.0, thought: "Solid film")
        };
        var episodes = new[]
        {
            Episode(1, 1, 1, rating: 9.0, thought: "Great ep"),
            Episode(1, AppConstants.SpecialsSeasonNumber, 1, rating: 10.0, thought: "Special"),
            Episode(2, 1, 1, rating: 7.0)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, movies, episodes, new Random(1));

        overview.BillboardHighlights.Should().NotContain(item =>
            item.Kind == StatsBillboardKind.Episode && item.SeasonNumber == AppConstants.SpecialsSeasonNumber);
        overview.BillboardHighlights[0].Kind.Should().Be(StatsBillboardKind.Show);
        overview.BillboardHighlights[0].Thought.Should().Be("Best show");
        overview.BillboardHighlights.Should().Contain(item =>
            item.Kind == StatsBillboardKind.Episode && item.Thought == "Great ep" && item.Subtitle!.StartsWith("S01E01"));
        overview.BillboardHighlights.Should().Contain(item =>
            item.Kind == StatsBillboardKind.Movie && item.Thought == "Solid film");
        overview.BillboardRandom.Select(item => item.IdentityKey)
            .Should()
            .BeEquivalentTo(overview.BillboardHighlights.Select(item => item.IdentityKey));
    }

    [Fact]
    public void Billboard_RandomIsRemainderWhenPoolExceedsHighlightSize()
    {
        var shows = Enumerable.Range(1, 10)
            .Select(index => Show(index, $"Show {index:00}", rating: 10.0 - index * 0.1))
            .ToArray();

        var overview = PersonalRatingOverviewBuilder.Build(shows, [], [], new Random(1));

        overview.BillboardHighlights.Should().HaveCount(PersonalRatingOverviewBuilder.BillboardHighlightSize);
        overview.BillboardRandom.Should().HaveCount(2);
        overview.BillboardHighlights.Select(item => item.MediaId).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        overview.BillboardRandom.Select(item => item.MediaId).Should().BeEquivalentTo([9L, 10L]);
    }

    [Fact]
    public void HallOfFame_IncludesBoundariesAndExcludesMidBand()
    {
        var shows = new[] { Show(1, "Bands") };
        var episodes = new[]
        {
            Episode(1, 1, 1, rating: 10.0),
            Episode(1, 1, 2, rating: 8.0),
            Episode(1, 1, 3, rating: 7.9),
            Episode(1, 1, 4, rating: 6.0),
            Episode(1, 1, 5, rating: 5.9),
            Episode(1, 1, 6, rating: 1.0)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, [], episodes);

        overview.TopEpisodes.Select(entry => entry.UserRating).Should().Equal(10.0, 8.0);
        overview.BottomEpisodes.Select(entry => entry.UserRating).Should().Equal(1.0, 5.9);
        overview.TopEpisodes.Should().NotContain(entry => entry.UserRating >= 6.0 && entry.UserRating <= 7.9);
        overview.BottomEpisodes.Should().NotContain(entry => entry.UserRating >= 6.0 && entry.UserRating <= 7.9);
    }

    [Fact]
    public void HallOfFame_ReturnsFullEligiblePoolBeyondTen()
    {
        var shows = new[] { Show(1, "Pool") };
        var episodes = Enumerable.Range(1, 12)
            .Select(index => Episode(1, 1, index, rating: 10.0 - index * 0.05))
            .ToArray();

        var overview = PersonalRatingOverviewBuilder.Build(shows, [], episodes);

        overview.TopEpisodes.Should().HaveCount(12);
        overview.TopEpisodes.Select(entry => entry.EpisodeNumber).Should().Equal(
            Enumerable.Range(1, 12));
        overview.TopEpisodes[0].UserRating.Should().BeGreaterThan(overview.TopEpisodes[^1].UserRating);
        overview.BottomEpisodes.Should().BeEmpty();
    }

    [Fact]
    public void HallOfFame_SpecialsUseSameEligibilityGates()
    {
        var shows = new[] { Show(1, "OVA") };
        var episodes = new[]
        {
            Episode(1, AppConstants.SpecialsSeasonNumber, 1, rating: 9.0),
            Episode(1, AppConstants.SpecialsSeasonNumber, 2, rating: 7.0),
            Episode(1, AppConstants.SpecialsSeasonNumber, 3, rating: 4.0)
        };

        var overview = PersonalRatingOverviewBuilder.Build(shows, [], episodes);

        overview.TopSpecials.Should().ContainSingle(entry => entry.UserRating == 9.0);
        overview.BottomSpecials.Should().ContainSingle(entry => entry.UserRating == 4.0);
        overview.TopEpisodes.Should().BeEmpty();
        overview.BottomEpisodes.Should().BeEmpty();
    }

    private static TrackedShow Show(
        long id,
        string title,
        double? rating = null,
        UserWatchStatus watchStatus = UserWatchStatus.None,
        string? thought = null) =>
        new()
        {
            Id = id,
            TmdbId = (int)id,
            Title = title,
            Rating = rating,
            WatchStatus = watchStatus,
            Thought = thought
        };

    private static TrackedMovie Movie(
        long id,
        string title,
        double? rating = null,
        UserWatchStatus watchStatus = UserWatchStatus.None,
        string? thought = null) =>
        new()
        {
            Id = id,
            TmdbId = (int)id,
            Title = title,
            Rating = rating,
            WatchStatus = watchStatus,
            Thought = thought
        };

    private static EpisodeUserRatingRow Episode(
        long showId,
        int season,
        int episode,
        double? rating,
        bool isHidden = false,
        string? thought = null) =>
        new()
        {
            ShowId = showId,
            SeasonNumber = season,
            EpisodeNumber = episode,
            Title = $"E{episode}",
            UserRating = rating,
            IsHidden = isHidden,
            Thought = thought
        };
}
