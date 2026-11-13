using System.IO;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Search;
using media_management_app.Services;
using MediaManager.App.Tests.Cleanup;
using Xunit;

namespace MediaManager.App.Tests.Services;

/// <summary>Startup move of the search backend from Settings into the recipe files (native-search P6 step 1).</summary>
public sealed class SearchBackendMigratorTests : IDisposable
{
    private readonly string _stateFolder = Path.Combine(Path.GetTempPath(), "mm-backend-migration-tests", Guid.NewGuid().ToString("N"));

    private (FakeSettingsService Settings, RecipeService Recipes) Create()
    {
        Directory.CreateDirectory(_stateFolder);
        var settings = new FakeSettingsService();
        settings.Current.StateFolder = _stateFolder;
        return (settings, new RecipeService(settings, new FakeAppLogger()));
    }

    private static SearchRecipe SaveDefaultRecipe(RecipeService recipes, MediaKind kind, string id)
    {
        var recipe = new SearchRecipe { RecipeId = id, Name = $"Recipe {id}", TargetKind = kind };
        recipes.SaveRecipe(recipe);
        // An older version saved recipes with "Default" (follow Settings).
        var path = recipes.GetRecipePath(id);
        var json = File.ReadAllText(path);
        File.WriteAllText(path, json.Replace("\"searchBackend\": \"Qbittorrent\"", "\"searchBackend\": \"Default\""));
        return recipe;
    }

    [Fact]
    public void OldSettingsBackend_IsWrittenIntoDefaultRecipes_AndNativeIsEnabled()
    {
        var (settings, recipes) = Create();
        SaveDefaultRecipe(recipes, MediaKind.TvEpisode, "r1");
        settings.Current.Search.Backend = SearchBackends.Both;
        var logger = new FakeAppLogger();

        var result = SearchBackendMigrator.Run(settings, recipes, logger);

        Assert.True(result.Ran);
        Assert.Equal(SearchBackends.Both, RecipeRuntimeSettings.GetSearchBackend(recipes.GetRecipeOrDefault("r1", MediaKind.TvEpisode)));
        Assert.True(settings.Current.Search.NativeEnabled);
        Assert.True(settings.Current.Search.RecipeBackendsMigrated);
        Assert.Contains(result.Changes, change => change.RecipeId == "r1" && change.NewBackend == SearchBackends.Both);
    }

    [Fact]
    public void SecondRun_ChangesNothing()
    {
        var (settings, recipes) = Create();
        settings.Current.Search.Backend = SearchBackends.Native;
        SearchBackendMigrator.Run(settings, recipes, new FakeAppLogger());
        SaveDefaultRecipe(recipes, MediaKind.Movie, "r2");

        var result = SearchBackendMigrator.Run(settings, recipes, new FakeAppLogger());

        Assert.False(result.Ran);
        Assert.Null(RecipeRuntimeSettings.GetSearchBackend(recipes.GetRecipeOrDefault("r2", MediaKind.Movie)));
    }

    [Fact]
    public void NewDefaultRecipes_StartOnQbittorrent()
    {
        var (_, recipes) = Create();

        foreach (var kind in new[] { MediaKind.Movie, MediaKind.TvEpisode, MediaKind.TvSeasonPack })
        {
            Assert.Equal(SearchBackends.Qbittorrent, RecipeRuntimeSettings.GetSearchBackend(recipes.GetDefaultRecipe(kind)));
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_stateFolder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
