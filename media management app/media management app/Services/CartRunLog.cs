using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Log lines for the steps a cart run takes after the search returns (recipe filtering, saving the order's result,
/// the run summary). Before these existed a run whose recipe had the candidate debug log off wrote nothing after
/// "Recipe search query succeeded N/N", so a finished run, a run with no candidates and a run that failed on an
/// exception all looked the same in the system log as a run that hung.
/// </summary>
public static class CartRunLog
{
    private const int MaxRejectReasons = 4;
    private const int MaxDetailLength = 300;

    /// <summary>One line after the recipe has scored every row of a dry run (cart, retry, Run now).</summary>
    public static string Evaluation(string targetTitle, IReadOnlyList<RecipeCandidateResult> evaluated, RecipeDryRunResult result)
    {
        var accepted = evaluated.Count(candidate => candidate.IsAccepted);
        var line = $"Recipe filtering for '{targetTitle}': rows={evaluated.Count}, accepted={accepted}, " +
                   $"rejected={evaluated.Count - accepted}, kept={result.AcceptedCandidates.Count}";
        if (result.BestCandidate is { } best)
        {
            line += $", top='{best.SearchResult.FileName}' score={best.TotalScore}";
        }

        var reasons = evaluated
            .Where(candidate => !candidate.IsAccepted)
            .GroupBy(candidate => candidate.RejectReason)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => $"{group.Key}={group.Count()}")
            .ToList();
        if (reasons.Count > 0)
        {
            var shown = reasons.Take(MaxRejectReasons).ToList();
            var more = reasons.Count > shown.Count ? $", +{reasons.Count - shown.Count} more" : string.Empty;
            line += $"; rejects: {string.Join(", ", shown)}{more}";
        }

        if (!string.IsNullOrWhiteSpace(result.SearchSourceProblem))
        {
            line += $"; source problem: {Trim(result.SearchSourceProblem)}";
        }

        return line.EndsWith('.') ? line : line + ".";
    }

    /// <summary>The order got candidates and they were saved to the cart.</summary>
    public static string OrderFound(TorrentCartOrder order, int candidateCount, string? backendNote) =>
        $"Cart order {Describe(order)}: saved {candidateCount} candidate(s).{Note(backendNote)}";

    /// <summary>The search finished but the recipe accepted nothing.</summary>
    public static string OrderNoCandidates(TorrentCartOrder order, string detail, string? backendNote) =>
        $"Cart order {Describe(order)}: no candidates. {Trim(detail)}{Note(backendNote)}";

    /// <summary>The order failed; the exception message is also what the order's status shows.</summary>
    public static string OrderFailed(TorrentCartOrder order, Exception exception) =>
        $"Cart order {Describe(order)} failed after search: {exception.GetType().Name}: {Trim(exception.Message)}";

    /// <summary>The line the status bar shows at the end of a run, written to the log too.</summary>
    public static string RunComplete(int candidateCount, int noCandidateCount, int failedCount) =>
        $"Search complete. Candidates={candidateCount}, No candidates={noCandidateCount}, Failed={failedCount}.";

    private static string Describe(TorrentCartOrder order)
    {
        var target = order.TargetKind switch
        {
            MediaKind.Movie => "movie",
            _ when order.EpisodeId is null && order.SeasonNumber is not null => $"pack S{order.SeasonNumber:00}",
            _ when order.SeasonNumber is not null && order.EpisodeNumber is not null => $"S{order.SeasonNumber:00}E{order.EpisodeNumber:00}",
            _ => order.TargetKind.ToString()
        };
        return $"id={order.Id} '{order.Title}' ({target})";
    }

    private static string Note(string? backendNote) =>
        string.IsNullOrWhiteSpace(backendNote) ? string.Empty : $" Backend: {Trim(backendNote)}";

    private static string Trim(string? text)
    {
        var value = (text ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return value.Length <= MaxDetailLength ? value : value[..MaxDetailLength] + "…";
    }
}
