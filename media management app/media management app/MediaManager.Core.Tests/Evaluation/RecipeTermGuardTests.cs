using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Evaluation;

public class RecipeTermGuardTests
{
    [Fact]
    public void Apply_ExcludeTermMatchingPrimaryTitle_IsSuspended()
    {
        var recipe = RecipeBuilder.Movie().ExcludeTerms("Cam", "TS").Build();

        var guard = RecipeTermGuard.Apply(recipe, "Cam", []);

        guard.Suspended.Should().ContainSingle()
            .Which.Should().Be(new SuspendedRecipeTerm(RecipeGuardedTermKind.ExcludeTerm, "Cam", "Cam"));
        Filter(guard.Recipe).ExcludeTerms.Should().Equal("TS");
    }

    [Fact]
    public void Apply_TitleOnlyContainingTermAsPartOfWord_KeepsTerm()
    {
        var recipe = RecipeBuilder.Movie().ExcludeTerms("CAM").Build();

        var guard = RecipeTermGuard.Apply(recipe, "El Camino: A Breaking Bad Movie", []);

        guard.HasSuspensions.Should().BeFalse();
        guard.Recipe.Should().BeSameAs(recipe);
    }

    [Fact]
    public void Apply_TermMatchingSearchableAlternativeTitle_IsSuspended()
    {
        var recipe = RecipeBuilder.TvEpisode().ExcludeTerms("Raw").Build();

        var guard = RecipeTermGuard.Apply(recipe, "Grave", ["Raw"]);

        guard.Suspended.Should().ContainSingle().Which.MatchedTitle.Should().Be("Raw");
        Filter(guard.Recipe).ExcludeTerms.Should().BeEmpty();
    }

    [Fact]
    public void Apply_TermMatchingRecipeAlias_IsSuspended()
    {
        var recipe = RecipeBuilder.TvEpisode().WithAliases("Ghost Hunt").BlockedGroups("Ghost").Build();

        var guard = RecipeTermGuard.Apply(recipe, "Some Other Title", []);

        guard.Suspended.Should().ContainSingle()
            .Which.Kind.Should().Be(RecipeGuardedTermKind.BlockedReleaseGroup);
        Filter(guard.Recipe).BlockedReleaseGroups.Should().BeEmpty();
    }

    [Fact]
    public void Apply_TermMatchingOnlySanitizedTitle_IsSuspended()
    {
        // The raw title "Project: X" does not contain "Project X"; the sanitized form does.
        var recipe = RecipeBuilder.Movie().ExcludeTerms("Project X").Build();

        var titles = RecipeTermGuard.CollectTitles(recipe, "Project: X", []);
        var guard = RecipeTermGuard.Apply(recipe, titles);

        titles.Should().Equal("Project: X", "Project X");
        guard.Suspended.Should().ContainSingle().Which.MatchedTitle.Should().Be("Project X");
    }

    [Fact]
    public void Apply_BlockedGroupMatchingTitle_IsSuspended()
    {
        var recipe = RecipeBuilder.TvEpisode().BlockedGroups("Erai-raws", "Ghost").Build();

        var guard = RecipeTermGuard.Apply(recipe, "Ghost in the Shell", []);

        guard.BlockedGroupCount.Should().Be(1);
        guard.ExcludeTermCount.Should().Be(0);
        Filter(guard.Recipe).BlockedReleaseGroups.Should().Equal("Erai-raws");
    }

    [Fact]
    public void Apply_DoesNotTouchIncludeOrPreferTermsOrStockRecipe()
    {
        var recipe = RecipeBuilder.Movie().IncludeTerms("Cam").ExcludeTerms("Cam").Build();
        Filter(recipe).PreferTerms = ["Cam"];

        var guard = RecipeTermGuard.Apply(recipe, "Cam", []);

        Filter(guard.Recipe).IncludeTerms.Should().Equal("Cam");
        Filter(guard.Recipe).PreferTerms.Should().Equal("Cam");
        Filter(guard.Recipe).ExcludeTerms.Should().BeEmpty();
        Filter(recipe).ExcludeTerms.Should().Equal("Cam");
        guard.Recipe.Should().NotBeSameAs(recipe);
    }

    [Fact]
    public void Apply_IsCaseSensitive()
    {
        var recipe = RecipeBuilder.Movie().ExcludeTerms("CAM").Build();

        var guard = RecipeTermGuard.Apply(recipe, "Cam", []);

        // "CAM" does not match the title "Cam", so it stays active.
        guard.HasSuspensions.Should().BeFalse();
    }

    [Fact]
    public void FormatSummaryAndHeader_DescribeSuspensions()
    {
        var recipe = RecipeBuilder.Movie().ExcludeTerms("Cam").BlockedGroups("Cam").Build();

        var guard = RecipeTermGuard.Apply(recipe, "Cam", []);

        guard.FormatSummary("Common Movie", "Cam (2018)").Should()
            .Contain("1 exclude term(s) and 1 blocked group(s)");
        guard.FormatDebugHeader("Cam (2018)").Should().HaveCount(4)
            .And.Contain(line => line.Contains("suspended exclude term 'Cam'"))
            .And.Contain(line => line.Contains("suspended blocked group 'Cam'"));
        RecipeTermGuard.Apply(recipe, "Other", []).FormatSummary("x", "y").Should().BeNull();
    }

    [Fact]
    public void EvaluateMovie_WithGuardedRecipe_AcceptsReleaseOfMovieNamedLikeExcludedTerm()
    {
        var titles = new FakeSearchTitleResolver { Titles = ["Cam"] };
        var sut = new CandidateEvaluationService(titles);
        var movie = TrackedShowBuilder.Movie("Cam", 2018);
        var recipe = RecipeBuilder.Movie().ExcludeTerms("Cam").Build();
        var release = TorrentResultBuilder.Magnet("Cam.2018.1080p.NF.WEB-DL.DDP5.1.x264-NTG");

        var stock = sut.EvaluateMovie(recipe, movie, release);
        var guarded = sut.EvaluateMovie(RecipeTermGuard.Apply(recipe, "Cam", []).Recipe, movie, release);

        stock.RejectReason.Should().Be(CandidateRejectReason.ExcludedTerm);
        guarded.IsAccepted.Should().BeTrue();
    }

    private static RecipeModuleConfig Filter(SearchRecipe recipe) =>
        RecipeCandidateFilter.GetFilterModule(recipe)!;
}
