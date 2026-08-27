using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class RecipeCandidateFilter
{
    public static (CandidateRejectReason Reason, string Detail) GetRejectReason(
        SearchRecipe recipe,
        TorrentSearchResult result,
        TorrentCandidateParseResult? parsed = null)
    {
        if (!result.CanAdd)
        {
            return (CandidateRejectReason.NotAddable, $"not addable link type '{result.LinkType}'");
        }

        if (LooksLikePluginError(result.FileName))
        {
            return (CandidateRejectReason.PluginError, "search plugin error row");
        }

        var filter = recipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.CandidateFilter && module.IsEnabled);
        if (filter is null)
        {
            return (CandidateRejectReason.None, string.Empty);
        }

        if (result.Seeders < filter.MinimumSeeders)
        {
            return (CandidateRejectReason.SeedersTooLow, $"seeders below threshold {filter.MinimumSeeders}");
        }

        if (filter.MinimumSizeBytes is not null &&
            result.FileSize > 0 &&
            result.FileSize < filter.MinimumSizeBytes.Value)
        {
            return (CandidateRejectReason.SizeTooSmall, "candidate is smaller than recipe minimum size");
        }

        if (filter.MaximumSizeBytes is not null && result.FileSize > filter.MaximumSizeBytes.Value)
        {
            return (CandidateRejectReason.SizeTooLarge, "candidate is larger than recipe maximum size");
        }

        parsed ??= TorrentCandidateParser.Parse(
            result.FileName,
            RecipeRuntimeSettings.UsesAnimeAbsoluteEpisodeNumbering(recipe));
        var detectedQuality = string.IsNullOrWhiteSpace(parsed.Quality)
            ? TorrentQuality.Detect(result.FileName)
            : parsed.Quality;
        if (!TorrentQuality.MatchesSelectedQuality(detectedQuality, filter.QualityAllowList))
        {
            return (
                CandidateRejectReason.QualityMismatch,
                $"does not match selected quality options: {string.Join(", ", filter.QualityAllowList)}");
        }

        var missingTerm = filter.IncludeTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .FirstOrDefault(term => !result.FileName.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (missingTerm is not null)
        {
            return (CandidateRejectReason.MissingIncludeTerm, $"missing include term '{missingTerm}'");
        }

        var excludedTerm = filter.ExcludeTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .FirstOrDefault(term => result.FileName.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (excludedTerm is not null)
        {
            return (CandidateRejectReason.ExcludedTerm, $"contains excluded term '{excludedTerm}'");
        }

        var blockedGroup = filter.BlockedReleaseGroups
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .FirstOrDefault(group => result.FileName.Contains(group, StringComparison.OrdinalIgnoreCase));
        if (blockedGroup is not null)
        {
            return (CandidateRejectReason.BlockedReleaseGroup, $"contains blocked group '{blockedGroup}'");
        }

        return (CandidateRejectReason.None, string.Empty);
    }

    public static RecipeModuleConfig? GetFilterModule(SearchRecipe recipe)
    {
        return recipe.Modules.FirstOrDefault(module =>
            module.BlockType == RecipeBlockType.CandidateFilter && module.IsEnabled);
    }

    public static bool LooksLikePluginError(string fileName)
    {
        return string.IsNullOrWhiteSpace(fileName) ||
               fileName.Contains("api key error", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("right-click this row", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("open description", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("jackett:", StringComparison.OrdinalIgnoreCase);
    }
}
