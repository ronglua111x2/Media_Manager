using System.Globalization;
using System.Text;
using media_management_app.Models;

namespace media_management_app.Services;

public static class HuntLogFormatter
{
    public static string FormatEpisodeLine(HuntEpisodeOutcome outcome)
    {
        var label = string.IsNullOrWhiteSpace(outcome.EpisodeLabel)
            ? outcome.ShowTitle
            : $"{outcome.ShowTitle} {outcome.EpisodeLabel}";

        if (outcome.Stage == HuntEpisodeStage.Succeeded)
        {
            var added = string.IsNullOrWhiteSpace(outcome.AddedCandidateName)
                ? string.Empty
                : $" → {outcome.AddedCandidateName}";
            return $"[HUNT] {label}: ADDED — search {outcome.SearchRows}, matched {outcome.RecipeMatched}, kept {outcome.PolicyKept}{added}";
        }

        var stage = FormatStage(outcome.Stage);
        var reason = string.IsNullOrWhiteSpace(outcome.FailureReason)
            ? outcome.FailureDetail
            : outcome.FailureReason;
        var funnel = $"search {outcome.SearchRows}, matched {outcome.RecipeMatched}, kept {outcome.PolicyKept}";
        var reasonSuffix = FormatReasonSuffix(reason);
        return $"[HUNT] {label}: FAILED ({stage}) — {funnel}{reasonSuffix}";
    }

    public static string FormatHumanSummary(string counterLine, IReadOnlyList<HuntEpisodeOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return counterLine;
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(counterLine))
        {
            builder.Append(counterLine.TrimEnd());
        }

        var failures = outcomes.Where(outcome => outcome.IsFailure).ToList();
        var added = outcomes.Count(outcome => outcome.Stage == HuntEpisodeStage.Succeeded);
        if (failures.Count == 0 && added == 0)
        {
            return builder.ToString();
        }

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        if (failures.Count > 0)
        {
            builder.Append(string.Join("; ", failures.Select(FormatCompactFailure)));
        }

        return builder.ToString();
    }

    public static string FormatRejectSummary(IReadOnlyDictionary<CandidateRejectReason, int> counts)
    {
        if (counts.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            ", ",
            counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}×{pair.Value}"));
    }

    public static string FormatOverrideRejectFailure(IReadOnlyDictionary<CandidateRejectReason, int> counts)
    {
        var summary = FormatRejectSummary(counts);
        return string.IsNullOrWhiteSpace(summary)
            ? "Rejected by override"
            : $"Rejected by override. {summary}";
    }

    public static string FormatSizeMiB(long bytes)
    {
        if (bytes <= 0)
        {
            return "unknown";
        }

        var mib = bytes / (1024d * 1024d);
        return mib.ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
    }

    public static string FormatEndedBy(string endedBy, int collectedRows)
    {
        if (string.Equals(endedBy, "timeout", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(endedBy, "idle-timeout", StringComparison.OrdinalIgnoreCase))
        {
            return $"{endedBy} (collected {collectedRows} rows)";
        }

        return endedBy;
    }

    private static string FormatCompactFailure(HuntEpisodeOutcome outcome)
    {
        var label = string.IsNullOrWhiteSpace(outcome.EpisodeLabel)
            ? outcome.ShowTitle
            : $"{outcome.ShowTitle} {outcome.EpisodeLabel}";
        var reason = outcome.FailureReason ?? FormatStage(outcome.Stage);
        return $"{label}: {reason}";
    }

    private static string FormatReasonSuffix(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? string.Empty : $": {reason}";
    }

    private static string FormatStage(HuntEpisodeStage stage)
    {
        return stage switch
        {
            HuntEpisodeStage.Search => "search",
            HuntEpisodeStage.RecipeMatch => "recipe",
            HuntEpisodeStage.Accept => "accept",
            HuntEpisodeStage.Add => "add",
            HuntEpisodeStage.Succeeded => "added",
            _ => stage.ToString().ToLowerInvariant()
        };
    }
}
