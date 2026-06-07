using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface IRecipeService
{
    IReadOnlyList<SearchRecipe> GetRecipes();

    SearchRecipe GetDefaultRecipe(MediaKind targetKind);

    SearchRecipe GetRecipeOrDefault(string? recipeId, MediaKind targetKind);

    SearchRecipe SaveRecipe(SearchRecipe recipe);

    SearchRecipe DuplicateRecipe(string recipeId);

    void DeleteRecipe(string recipeId);

    SearchRecipe ImportRecipe(string filePath);

    string ExportRecipe(string recipeId, string folderPath);

    string GetRecipePath(string recipeId);
}
