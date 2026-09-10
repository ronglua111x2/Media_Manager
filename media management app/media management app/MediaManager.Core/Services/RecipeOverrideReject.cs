using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class RecipeOverrideReject
{
    public static bool IsOverrideReject(
        RecipeExecutionOverrides? overrides,
        SearchRecipe? stockRecipe,
        CandidateRejectReason reason,
        int seeders,
        long fileSize)
    {
        if (overrides is null || stockRecipe is null)
        {
            return false;
        }

        var stockFilter = stockRecipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.CandidateFilter && module.IsEnabled);

        if (reason == CandidateRejectReason.SeedersTooLow && overrides.MinSeeders is not null)
        {
            var stockMinSeeders = stockFilter?.MinimumSeeders ?? 0;
            return seeders >= stockMinSeeders;
        }

        if (reason == CandidateRejectReason.SizeTooSmall && overrides.OverrideMinSize)
        {
            var stockMinBytes = stockFilter?.MinimumSizeBytes;
            return stockMinBytes is null or <= 0 || fileSize <= 0 || fileSize >= stockMinBytes.Value;
        }

        return false;
    }

    public static Dictionary<CandidateRejectReason, int> CountOverrideRejects(
        IReadOnlyList<RecipeCandidateResult> evaluated,
        RecipeExecutionOverrides? overrides,
        SearchRecipe? stockRecipe)
    {
        var counts = new Dictionary<CandidateRejectReason, int>();
        if (overrides is null || stockRecipe is null || evaluated.Count == 0)
        {
            return counts;
        }

        foreach (var item in evaluated)
        {
            if (item.IsAccepted)
            {
                continue;
            }

            if (!IsOverrideReject(
                    overrides,
                    stockRecipe,
                    item.RejectReason,
                    item.SearchResult.Seeders,
                    item.SearchResult.FileSize))
            {
                continue;
            }

            counts[item.RejectReason] = counts.GetValueOrDefault(item.RejectReason) + 1;
        }

        return counts;
    }
}
