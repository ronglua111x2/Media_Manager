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
        string query)
    {
        if (requestedNames.Count == 0)
        {
            return;
        }

        var counts = results
            .GroupBy(result => result.EngineName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var engineName in requestedNames)
        {
            if (counts.TryGetValue(engineName, out var count) && count > 0)
            {
                continue;
            }

            logger.Info(
                $"Search engine '{engineName}' returned no results. Query='{query}'.",
                LogTarget.All);
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
