using media_management_app.Models;

namespace media_management_app.Services;

public static class QueryHuntEstimateInputs
{
    public const string HuntEstimateInputChanged = nameof(HuntEstimateInputChanged);

    public const string AffectsHuntEstimateHint = "Affects hunt estimate.";

    public static bool AffectsModuleEnabled(RecipeBlockType blockType) =>
        blockType is RecipeBlockType.Identity or RecipeBlockType.QueryBuilder;

    public static bool AffectsExtensionKey(string key) =>
        key is RecipeRuntimeSettings.UseLibraryEnglishTitlesKey
            or RecipeRuntimeSettings.MaxLibraryAlternativeTitlesForSearchKey
            or RecipeRuntimeSettings.SkipDefaultTitleKey;
}
