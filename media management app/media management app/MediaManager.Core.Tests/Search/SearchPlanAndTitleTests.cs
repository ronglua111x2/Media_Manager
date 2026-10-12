using FluentAssertions;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Search;

public class SearchTitleResolverTests
{
    private readonly SearchTitleResolver _sut = new();

    [Fact]
    public void Resolve_IdentityDisabled_ReturnsPrimaryOnly()
    {
        var recipe = RecipeBuilder.TvEpisode().Build();
        recipe.Modules.First(m => m.BlockType == media_management_app.Models.RecipeBlockType.Identity).IsEnabled = false;

        var titles = _sut.Resolve(_sut.CreateRequest(recipe, "Jujutsu Kaisen", ["JJK"]));

        titles.Should().Equal("Jujutsu Kaisen");
    }

    [Fact]
    public void Resolve_ManualAliases_AreExpanded()
    {
        var recipe = RecipeBuilder.TvEpisode().WithAliases("JJK", "Sorcery Fight").Build();

        var titles = _sut.Resolve(_sut.CreateRequest(recipe, "Jujutsu Kaisen", []));

        titles.Should().Contain("Jujutsu Kaisen");
        titles.Should().Contain("JJK");
        titles.Should().Contain("Sorcery Fight");
    }

    [Fact]
    public void Resolve_SkipDefaultTitle_OmitsPrimary()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .WithAliases("JJK")
            .SkipDefaultTitle()
            .Build();

        var titles = _sut.Resolve(_sut.CreateRequest(recipe, "Jujutsu Kaisen", []));

        titles.Should().Equal("JJK");
    }

    [Fact]
    public void Resolve_LibraryEnglishTitles_IncludesDistinctAlts()
    {
        var recipe = RecipeBuilder.TvEpisode().UseLibraryEnglishTitles().Build();

        var titles = _sut.Resolve(_sut.CreateRequest(
            recipe,
            "Jujutsu Kaisen",
            ["Sorcery Fight", "Jujutsu Kaisen"]));

        titles.Should().Contain("Jujutsu Kaisen");
        titles.Should().Contain("Sorcery Fight");
    }
}

public class SearchPlanBuilderTests
{
    [Fact]
    public void BuildEpisodeQueries_DefaultTemplate_ContainsSeasonEpisodeAndQuality()
    {
        var queries = BuildEpisode();

        queries.Should().Contain(q => q.Contains("S03E01", StringComparison.OrdinalIgnoreCase));
        queries.Should().Contain(q => q.Contains("1080p", StringComparison.OrdinalIgnoreCase));
        queries.Should().Contain(q => q.Contains("Jujutsu Kaisen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildEpisodeQueries_CustomQuery_IsAppended()
    {
        var recipe = RecipeBuilder.TvEpisode().CustomQueries("{title} batch").Build();

        var queries = BuildEpisode(recipe);

        queries.Should().Contain(q => q.Contains("batch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildEpisodeQueries_Sanitize_StripsUnsafeCharacters()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .QueryTemplates(["{title}: S{season:00}E{episode:00}!"])
            .SanitizeQuery(true)
            .Build();

        var queries = BuildEpisode(recipe);

        queries.Should().OnlyContain(q => !q.Contains(':') && !q.Contains('!'));
    }

    [Fact]
    public void BuildEpisodeQueries_Aliases_ProduceMultipleQueries()
    {
        var recipe = RecipeBuilder.TvEpisode().WithAliases("JJK").Build();

        var queries = BuildEpisode(recipe);

        queries.Should().Contain(q => q.StartsWith("Jujutsu Kaisen", StringComparison.OrdinalIgnoreCase));
        queries.Should().Contain(q => q.StartsWith("JJK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildMovieQueries_IncludesYearAndQuality()
    {
        var recipe = RecipeBuilder.Movie().Build();
        var movie = TrackedShowBuilder.Movie("Your Name", 2016);
        var sut = new SearchPlanBuilder(new SearchTitleResolver());

        var queries = sut.BuildMovieQueries(recipe, movie);

        queries.Should().Contain(q => q.Contains("2016") && q.Contains("1080p"));
        queries.Should().Contain(q => q.Contains("Your Name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildShowSnapshotQueries_DropsSeasonTemplates()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .QueryTemplates(["{title} S{season:00}E{episode:00}", "{title} {year}"])
            .Build();
        var show = TrackedShowBuilder.Show();
        var sut = new SearchPlanBuilder(new SearchTitleResolver());

        var queries = sut.BuildShowSnapshotQueries(recipe, show);

        queries.Should().OnlyContain(q => !q.Contains("S03E01", StringComparison.OrdinalIgnoreCase));
        queries.Should().Contain(q => q.Contains("2020"));
    }

    [Fact]
    public void BuildEpisodeQueries_SkipsRetiredAudioTemplateAndKeepsValidRows()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .QueryTemplates(
                "{title} S{season:00}E{episode:00} {quality} {audio}",
                "{title} {year}")
            .Build();

        var queries = BuildEpisode(recipe);

        queries.Should().NotContain(q => q.Contains("audio", StringComparison.OrdinalIgnoreCase));
        queries.Should().Contain(q => q.Contains("2020", StringComparison.OrdinalIgnoreCase));
        queries.Should().NotContain(q => q.Contains("S03E01", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildEpisodeQueries_AllRejectedTemplates_FallBackToDefaults()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .QueryTemplates("{title} {audio}")
            .Build();

        var queries = BuildEpisode(recipe);

        queries.Should().NotBeEmpty();
        queries.Should().NotContain(q => q.Contains("{audio}", StringComparison.OrdinalIgnoreCase));
        queries.Should().Contain(q => q.Contains("S03E01", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildEpisodeQueries_DoesNotInsertQueryPreferredAudio()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .QueryTemplates("{title} S{season:00}E{episode:00} {quality}")
            .Build();
        recipe.Modules.First(module => module.BlockType == media_management_app.Models.RecipeBlockType.QueryBuilder)
            .PreferredAudioCodec = "Atmos";

        var queries = BuildEpisode(recipe);

        queries.Should().OnlyContain(q => !q.Contains("Atmos", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> BuildEpisode(media_management_app.Models.SearchRecipe? recipe = null)
    {
        recipe ??= RecipeBuilder.TvEpisode().Build();
        var sut = new SearchPlanBuilder(new SearchTitleResolver());
        return sut.BuildEpisodeQueries(recipe, TrackedShowBuilder.Show(), TrackedShowBuilder.Episode(3, 1));
    }
}
