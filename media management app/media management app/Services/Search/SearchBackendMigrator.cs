using media_management_app.Common;
using media_management_app.Search;

namespace media_management_app.Services;

/// <summary>
/// Runs <see cref="SearchBackendMigration"/> once at startup (native-search P6 step 1): recipes on "Default" get the
/// backend they were effectively using, and native search is turned on when it was in use. Each changed recipe is
/// logged. Settings are marked migrated only after every changed recipe was saved, so a failed save retries next start.
/// </summary>
public static class SearchBackendMigrator
{
    public static SearchBackendMigrationResult Run(ISettingsService settingsService, IRecipeService recipeService, IAppLogger logger)
    {
        var search = settingsService.Current.Search ??= new NativeSearchSettings();
        if (search.RecipeBackendsMigrated)
        {
            return new SearchBackendMigrationResult(false, [], false);
        }

        var recipes = recipeService.GetRecipes();
        var result = SearchBackendMigration.Migrate(search, recipes);
        var failed = 0;
        foreach (var change in result.Changes)
        {
            var recipe = recipes.First(item => item.RecipeId == change.RecipeId);
            try
            {
                recipeService.SaveRecipe(recipe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                logger.Warning($"[Search] could not save recipe '{recipe.Name}' while moving the search backend into it: {ex.Message}", LogTarget.All);
            }
        }

        logger.Info(
            $"[Search] search backend moved from Settings into recipes (old Settings backend: {search.Backend}" +
            $"{(search.BackendByKind.Count == 0 ? "" : $", per kind: {string.Join(", ", search.BackendByKind.Select(pair => $"{pair.Key}={pair.Value}"))}")}). " +
            $"{result.Changes.Count} recipe(s) changed; native search {(search.NativeEnabled ? "enabled" : "disabled")}.",
            LogTarget.All);
        foreach (var line in result.Describe())
        {
            logger.Info(line, LogTarget.All);
        }

        if (failed == 0)
        {
            search.RecipeBackendsMigrated = true;
        }

        try
        {
            settingsService.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning($"[Search] could not save settings after moving the search backend into recipes: {ex.Message}", LogTarget.All);
        }

        return result;
    }
}
