using FluentAssertions;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Recipes;

public class RecipeRuntimeSettingsTests
{
    [Fact]
    public void GetMaxCandidatesPerFetch_NoOverride_UsesRecipeValue()
    {
        var recipe = CreateRecipe("7");

        var result = RecipeRuntimeSettings.GetMaxCandidatesPerFetch(
            recipe,
            new AutoTorrentSettings { MaxCandidatesPerFetch = 3 },
            overrides: null);

        result.Should().Be(7);
    }

    [Fact]
    public void GetMaxCandidatesPerFetch_Override_ReplacesRecipeValue()
    {
        var recipe = CreateRecipe("7");

        var result = RecipeRuntimeSettings.GetMaxCandidatesPerFetch(
            recipe,
            new AutoTorrentSettings { MaxCandidatesPerFetch = 3 },
            new RecipeExecutionOverrides { MaxCandidates = 12 });

        result.Should().Be(12);
    }

    [Theory]
    [InlineData(-10, 1)]
    [InlineData(0, 1)]
    [InlineData(100, 20)]
    public void GetMaxCandidatesPerFetch_Override_ClampsToCartRange(int value, int expected)
    {
        var result = RecipeRuntimeSettings.GetMaxCandidatesPerFetch(
            CreateRecipe("7"),
            new AutoTorrentSettings(),
            new RecipeExecutionOverrides { MaxCandidates = value });

        result.Should().Be(expected);
    }

    [Fact]
    public void WithCartOverrides_EnabledMinSeeders_ReplacesRecipeFilterWithoutMutatingSource()
    {
        var recipe = RecipeBuilder.TvEpisode().MinimumSeeders(1).Build();

        var result = RecipeRuntimeSettings.WithCartOverrides(
            recipe,
            new RecipeExecutionOverrides { MinSeeders = 10 });

        RecipeCandidateFilter.GetFilterModule(result)!.MinimumSeeders.Should().Be(10);
        RecipeCandidateFilter.GetFilterModule(recipe)!.MinimumSeeders.Should().Be(1);
    }

    [Fact]
    public void WithCartOverrides_DisabledStoredValues_LeaveRecipeUnchanged()
    {
        var recipe = RecipeBuilder.TvEpisode().MinimumSeeders(8).Build();

        var result = RecipeRuntimeSettings.WithCartOverrides(
            recipe,
            new RecipeExecutionOverrides { MinSeeders = null, OverrideMinSize = false });

        result.Should().BeSameAs(recipe);
    }

    [Fact]
    public void WithCartOverrides_ZeroGb_ClearsRecipeMinimumSize()
    {
        var recipe = RecipeBuilder.TvEpisode().SizeRange(5L * 1024 * 1024 * 1024, null).Build();

        var result = RecipeRuntimeSettings.WithCartOverrides(
            recipe,
            new RecipeExecutionOverrides { OverrideMinSize = true, MinSizeBytes = null });

        RecipeCandidateFilter.GetFilterModule(result)!.MinimumSizeBytes.Should().BeNull();
        RecipeCandidateFilter.GetFilterModule(recipe)!.MinimumSizeBytes.Should().Be(5L * 1024 * 1024 * 1024);
    }

    [Fact]
    public void WithCartOverrides_DebugOnly_ClonesSearchFlagWithoutMutatingSource()
    {
        var recipe = RecipeBuilder.TvEpisode().Build();
        RecipeRuntimeSettings.GetEnableCandidateDebugLog(recipe).Should().BeFalse();

        var result = RecipeRuntimeSettings.WithCartOverrides(
            recipe,
            new RecipeExecutionOverrides { EnableCandidateDebugLog = true });

        result.Should().NotBeSameAs(recipe);
        RecipeRuntimeSettings.GetEnableCandidateDebugLog(result).Should().BeTrue();
        RecipeRuntimeSettings.GetEnableCandidateDebugLog(recipe).Should().BeFalse();
    }

    [Fact]
    public void GetEnableCandidateDebugLog_Override_WinsOverRecipe()
    {
        var recipe = CreateRecipe("7");
        recipe.Modules[0].ExtensionData[RecipeRuntimeSettings.EnableCandidateDebugLogKey] = bool.FalseString;

        RecipeRuntimeSettings.GetEnableCandidateDebugLog(
            recipe,
            new RecipeExecutionOverrides { EnableCandidateDebugLog = true }).Should().BeTrue();
    }

    [Fact]
    public void GetEnableCandidateDebugLog_NoOverride_UsesRecipe()
    {
        var recipe = CreateRecipe("7");
        recipe.Modules[0].ExtensionData[RecipeRuntimeSettings.EnableCandidateDebugLogKey] = bool.TrueString;

        RecipeRuntimeSettings.GetEnableCandidateDebugLog(recipe, overrides: null).Should().BeTrue();
        RecipeRuntimeSettings.GetEnableCandidateDebugLog(
            recipe,
            new RecipeExecutionOverrides { EnableCandidateDebugLog = false }).Should().BeFalse();
    }

    private static SearchRecipe CreateRecipe(string maxCandidates)
    {
        return new SearchRecipe
        {
            TargetKind = MediaKind.TvEpisode,
            Modules =
            [
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.SearchSource,
                    ExtensionData = new Dictionary<string, string>
                    {
                        [RecipeRuntimeSettings.MaxCandidatesPerFetchKey] = maxCandidates
                    }
                }
            ]
        };
    }
}
