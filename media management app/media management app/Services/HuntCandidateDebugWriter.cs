using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class HuntCandidateDebugWriter
{
    public static CartCandidateDebugSession? TryCreateSession(
        SearchRecipe recipe,
        ISettingsService settingsService,
        IAppLogger logger)
    {
        if (!RecipeRuntimeSettings.GetEnableCandidateDebugLog(recipe))
        {
            return null;
        }

        var session = new CartCandidateDebugSession(
            settingsService.Current.StateFolder,
            settingsService.Current.Logs.MaxLinesPerFile);
        var debugLogFolder = Path.Combine(settingsService.Current.StateFolder, AppConstants.LogFolderName);
        logger.Info(
            $"Cart debug mode is enabled for recipe '{recipe.Name}'. Candidate debug log will be written under '{debugLogFolder}'.",
            LogTarget.All);
        return session;
    }

    public static void WriteRecipeEvaluation(
        CartCandidateDebugSession session,
        SearchRecipe recipe,
        string targetTitle,
        MediaKind targetKind,
        IReadOnlyList<string> queries,
        IReadOnlyList<TorrentSearchResult> searchResults,
        IReadOnlyList<RecipeCandidateResult> evaluated,
        int timeoutSeconds)
    {
        session.WriteLine(
            $"Media='{SanitizeForLog(targetTitle)}' Recipe='{SanitizeForLog(recipe.Name)}' RecipeId='{recipe.RecipeId}' Target='{targetKind}'");
        session.WriteLine($"Queries={queries.Count} => {string.Join(" | ", queries.Select(SanitizeForLog))}");
        session.WriteLine(
            $"SearchResults={searchResults.Count} TimeoutSeconds={timeoutSeconds} Accepted={evaluated.Count(item => item.IsAccepted)} Rejected={evaluated.Count(item => !item.IsAccepted)}");
        foreach (var summary in evaluated
                     .Where(item => !item.IsAccepted)
                     .GroupBy(item => item.RejectReason)
                     .OrderByDescending(group => group.Count()))
        {
            session.WriteLine($"RejectSummary reason={summary.Key} count={summary.Count()}");
        }

        foreach (var item in evaluated)
        {
            var result = item.SearchResult;
            var sizeGb = result.FileSize > 0
                ? (result.FileSize / (1024d * 1024d * 1024d)).ToString("0.00")
                : "unknown";
            var verdict = item.IsAccepted ? "ACCEPT" : "REJECT";
            var detail = item.IsAccepted ? "-" : SanitizeForLog(item.RejectDetail);
            session.WriteLine(
                $"{verdict} reason={item.RejectReason} detail='{detail}' quality='{TorrentQuality.Detect(result.FileName)}' sizeGB={sizeGb} seeders={result.Seeders} " +
                $"Q={item.QualityScore} A={item.AudioScore} Pref={item.PreferTermsScore} Size={item.SizeScore} Total={item.TotalScore} " +
                $"engine='{SanitizeForLog(result.EngineName)}' linkType='{result.LinkType}' name='{SanitizeForLog(result.FileName)}'");
        }
    }

    public static void WriteHuntMatch(
        CartCandidateDebugSession session,
        SearchRecipe recipe,
        string showTitle,
        string episodeLabel,
        int searchRows,
        IReadOnlyList<EpisodeFetchCandidate> recipeMatched)
    {
        session.WriteLine(
            $"HuntMatch Media='{SanitizeForLog(showTitle)}' Episode='{SanitizeForLog(episodeLabel)}' Recipe='{SanitizeForLog(recipe.Name)}' RecipeId='{recipe.RecipeId}'");
        session.WriteLine($"SearchRows={searchRows} RecipeMatched={recipeMatched.Count}");
        foreach (var candidate in recipeMatched)
        {
            session.WriteLine(
                $"MATCH size={HuntLogFormatter.FormatSizeMiB(candidate.FileSize)} seeders={candidate.Seeders} quality='{SanitizeForLog(candidate.QualityLabel)}' " +
                $"engine='{SanitizeForLog(candidate.PluginName)}' name='{SanitizeForLog(candidate.FileName)}'");
        }
    }

    public static void WriteHuntPolicy(CartCandidateDebugSession session, HuntEpisodeOutcome outcome)
    {
        var rejectSummary = HuntLogFormatter.FormatPolicyRejectSummary(outcome.PolicyRejectCounts);
        session.WriteLine(
            $"HuntPolicy Episode='{SanitizeForLog(outcome.EpisodeLabel)}' RecipeMatched={outcome.RecipeMatched} PolicyKept={outcome.PolicyKept} " +
            $"Stage={outcome.Stage} Rejects={rejectSummary}");
        if (outcome.PolicyMinFileSizeMb is > 0)
        {
            session.WriteLine($"PolicyThreshold MinFileSizeMb={outcome.PolicyMinFileSizeMb.Value} bestRejected={HuntLogFormatter.FormatSizeMiB(outcome.BestRejectedFileSize ?? 0)}");
        }
    }

    public static void LogSessionPath(IAppLogger logger, CartCandidateDebugSession session)
    {
        logger.Info($"Cart debug log: {session.FirstFilePath}", LogTarget.All);
    }

    public static string SanitizeForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Replace("'", "''")
            .Trim();
    }
}
