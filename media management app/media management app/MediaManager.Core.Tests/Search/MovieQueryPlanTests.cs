using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Search;

/// <summary>native-search P1: movie query building (SRCH-11 year-less variant, SRCH-12 alt titles, SRCH-13 apostrophes).</summary>
public class MovieQueryPlanTests
{
    private readonly SearchPlanBuilder _sut = new(new SearchTitleResolver());

    [Fact]
    public void YearTemplate_AlsoSearchesWithoutYear_AfterTheYearForms()
    {
        var recipe = RecipeBuilder.Movie().Build();

        var queries = _sut.BuildMovieQueries(recipe, TrackedShowBuilder.Movie("Coherence", 2014));

        queries.Should().Equal("Coherence 2014 1080p", "Coherence 1080p");
    }

    [Fact]
    public void MovieRecipe_WithoutSetting_SearchesLibraryAlternativeTitles()
    {
        var recipe = RecipeBuilder.Movie().Build();
        recipe.Modules.First(m => m.BlockType == RecipeBlockType.QueryBuilder).QueryTemplates = ["{title}"];

        var queries = _sut.BuildMovieQueries(recipe, TrackedShowBuilder.Movie("Liz and the Blue Bird", 2018, "Liz to Aoi Tori"));

        queries.Should().Contain("Liz and the Blue Bird").And.Contain("Liz to Aoi Tori");
    }

    [Fact]
    public void MovieRecipe_ExplicitlyOff_DoesNotSearchAlternativeTitles()
    {
        var recipe = RecipeBuilder.Movie().Build();
        recipe.Modules.First(m => m.BlockType == RecipeBlockType.QueryBuilder).QueryTemplates = ["{title}"];
        recipe.Modules.First(m => m.BlockType == RecipeBlockType.Identity)
            .ExtensionData[RecipeRuntimeSettings.UseLibraryEnglishTitlesKey] = bool.FalseString;

        var queries = _sut.BuildMovieQueries(recipe, TrackedShowBuilder.Movie("Liz and the Blue Bird", 2018, "Liz to Aoi Tori"));

        queries.Should().Equal("Liz and the Blue Bird");
    }

    [Fact]
    public void ApostropheTitle_SearchesJoinedFormFirst_ThenSplitForm()
    {
        var recipe = RecipeBuilder.Movie().Build();
        recipe.Modules.First(m => m.BlockType == RecipeBlockType.QueryBuilder).QueryTemplates = ["{title} {year}"];

        var queries = _sut.BuildMovieQueries(recipe, TrackedShowBuilder.Movie("Schindler's List", 1993));

        queries.Should().Equal("Schindlers List 1993", "Schindlers List", "Schindler s List 1993", "Schindler s List");
    }
}
