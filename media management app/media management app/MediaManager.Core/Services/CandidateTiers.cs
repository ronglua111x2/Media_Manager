using media_management_app.Models;

namespace media_management_app.Services;

public enum CandidateTier
{
    Rejected = 0,

    /// <summary>Passes the recipe filters.</summary>
    Accepted = 1,

    /// <summary>Accepted, in the recipe's two best allowed qualities, with enough seeders.</summary>
    Good = 2,

    /// <summary>Good, and matching something the recipe prefers (prefer term, release group or engine).</summary>
    Excellent = 3
}

/// <summary>
/// "How good is this candidate for this recipe" (native-search plan 08 §Q1). Counting accepted candidates
/// alone was misleading (Jujutsu Kaisen 0: 168 "accepted" were mostly mismatched TV episodes), so search
/// changes are measured by Good/Excellent candidates.
/// </summary>
public static class CandidateTiers
{
    public const int MinimumGoodSeeders = 5;

    public static CandidateTier Classify(SearchRecipe recipe, RecipeCandidateResult result)
    {
        if (!result.IsAccepted)
        {
            return CandidateTier.Rejected;
        }

        var filter = RecipeCandidateFilter.GetFilterModule(recipe);
        var row = result.SearchResult;
        var minimumSeeders = Math.Max(filter?.MinimumSeeders ?? 0, MinimumGoodSeeders);
        if (row.Seeders < minimumSeeders || !IsTopQuality(filter?.QualityAllowList, TorrentQuality.Detect(row.FileName)))
        {
            return CandidateTier.Accepted;
        }

        var preferred = result.PreferTermsScore > 0 ||
                        (filter?.PreferredReleaseGroups ?? []).Any(group => ReleaseTermMatcher.Matches(row.FileName, group)) ||
                        RecipeRuntimeSettings.ResolveEngineRankScore(row.EngineName, filter) > 0;
        return preferred ? CandidateTier.Excellent : CandidateTier.Good;
    }

    private static bool IsTopQuality(IReadOnlyList<string>? allowList, string? quality)
    {
        var rank = TorrentQuality.GetRank(quality ?? string.Empty);
        if (rank <= 0)
        {
            return false;
        }

        var allowedRanks = (allowList ?? [])
            .Select(value => TorrentQuality.GetRank(value))
            .Where(value => value > 0)
            .Distinct()
            .OrderByDescending(value => value)
            .Take(2)
            .ToList();
        return allowedRanks.Count == 0
            ? rank >= TorrentQuality.GetRank("1080p")
            : allowedRanks.Contains(rank);
    }
}
