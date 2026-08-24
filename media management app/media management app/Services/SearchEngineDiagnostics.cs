using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public static class SearchEngineDiagnostics
{
    public static string BuildEngineSummary(IEnumerable<TorrentSearchResult> results)
    {
        return string.Join(", ",
            results
                .GroupBy(result => string.IsNullOrWhiteSpace(result.EngineName) ? "?" : result.EngineName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Key}:{group.Count()}"));
    }

    public static void LogEmptyEngines(
        IAppLogger logger,
        IReadOnlyList<string> requestedNames,
        IEnumerable<TorrentSearchResult> results,
        string query,
        LogTarget emptyEngineTarget = LogTarget.All)
    {
        if (requestedNames.Count == 0)
        {
            return;
        }

        var resultList = results as IList<TorrentSearchResult> ?? results.ToList();
        var counts = resultList
            .GroupBy(result => result.EngineName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var emptyNames = requestedNames
            .Where(engineName => !counts.TryGetValue(engineName, out var count) || count <= 0)
            .ToList();
        if (emptyNames.Count == 0)
        {
            return;
        }

        var allRequestedEmpty = emptyNames.Count == requestedNames.Count && resultList.Count == 0;
        var target = allRequestedEmpty
            ? LogTarget.All
            : emptyEngineTarget == LogTarget.All
                ? LogTarget.File
                : emptyEngineTarget;

        foreach (var engineName in emptyNames)
        {
            var message = $"Search engine '{engineName}' returned no results. Query='{query}'.";
            if (allRequestedEmpty)
            {
                logger.Info(message, target);
            }
            else
            {
                logger.Debug(message, target);
            }
        }
    }

    public static string BuildEngineSummaryIncludingEmpty(
        IReadOnlyList<string> requestedNames,
        IEnumerable<TorrentSearchResult> results)
    {
        var counts = results
            .GroupBy(result => string.IsNullOrWhiteSpace(result.EngineName) ? "?" : result.EngineName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var parts = requestedNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => $"{name}:{(counts.TryGetValue(name, out var count) ? count : 0)}")
            .ToList();

        foreach (var extra in counts.Keys.Where(name => !requestedNames.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            parts.Add($"{extra}:{counts[extra]}");
        }

        return string.Join(", ", parts);
    }
}
