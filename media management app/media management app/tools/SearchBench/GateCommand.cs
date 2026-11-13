using System.Text.Json;

namespace SearchBench;

/// <summary>
/// SearchBench gate [--state D:\MediaManagerState] [--since 2026-11-12]
///
/// Reads &lt;state&gt;/logs/search-compare.jsonl, written by the app for every search in "Both" mode, and computes
/// the native-search P6 cutover criteria on the user's real searches (docs/planning/native-search/08 §Q5):
/// Good candidates native vs qBittorrent, "#1 not worse", and time. Read-only.
/// </summary>
public static class GateCommand
{
    public static int Run(Dictionary<string, string> args)
    {
        var state = args.GetValueOrDefault("state") ?? @"D:\MediaManagerState";
        var path = Path.Combine(state, "logs", "search-compare.jsonl");
        if (!File.Exists(path))
        {
            Console.WriteLine($"No {path}. Set Search backend to Both (Settings or recipe) and use the app for a while.");
            return 1;
        }

        var since = DateTimeOffset.TryParse(args.GetValueOrDefault("since"), out var parsed) ? parsed : DateTimeOffset.MinValue;
        var rows = new List<JsonElement>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement.Clone();
            if (root.GetProperty("utc").GetDateTimeOffset() >= since)
            {
                rows.Add(root);
            }
        }

        Console.WriteLine("| Kind | Searches | Evaluated | Native good ≥ 90% of qBit | #1 not worse | Avg good native / qBit | Median s native / qBit |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        // A failed qBittorrent search is no evidence for native (audit 07c NS-16): those records are counted apart.
        static bool QbitFailed(JsonElement row) =>
            row.GetProperty("qbittorrent").TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String;
        static bool FromMemo(JsonElement row) =>
            row.GetProperty("native").TryGetProperty("fromMemo", out var memo) && memo.ValueKind == JsonValueKind.True;
        var failed = rows.Count(QbitFailed);
        if (failed > 0)
        {
            Console.WriteLine($"{failed} record(s) with a failed qBittorrent search are left out.");
        }

        rows = rows.Where(row => !QbitFailed(row)).ToList();
        foreach (var group in rows.GroupBy(row => row.GetProperty("kind").GetString() ?? "?"))
        {
            var evaluated = group.Where(row => row.GetProperty("evaluated").GetBoolean()).ToList();
            int Good(JsonElement row, string side) => row.GetProperty(side).GetProperty("good").GetInt32();
            int? Top(JsonElement row, string side) =>
                row.GetProperty(side).GetProperty("topScore").ValueKind == JsonValueKind.Number ? row.GetProperty(side).GetProperty("topScore").GetInt32() : null;
            var coverageOk = evaluated.Count(row => Good(row, "native") >= 0.9 * Good(row, "qbittorrent"));
            var topOk = evaluated.Count(row => (Top(row, "native") ?? int.MinValue) >= (Top(row, "qbittorrent") ?? int.MinValue));
            double Median(IEnumerable<double> values)
            {
                var sorted = values.OrderBy(v => v).ToList();
                return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
            }

            // Memo answers take ~0 ms and would flatter the median.
            var nativeMs = Median(group.Where(row => !FromMemo(row)).Select(row => row.GetProperty("native").GetProperty("ms").GetDouble()));
            var qbitMs = Median(group.Select(row => row.GetProperty("qbittorrent").GetProperty("ms").GetDouble()));
            var pct = (int count) => evaluated.Count == 0 ? "-" : $"{100.0 * count / evaluated.Count:0}% ({count}/{evaluated.Count})";
            Console.WriteLine(
                $"| {group.Key} | {group.Count()} | {evaluated.Count} | {pct(coverageOk)} | {pct(topOk)} | " +
                $"{(evaluated.Count == 0 ? "-" : $"{evaluated.Average(r => Good(r, "native")):0.0} / {evaluated.Average(r => Good(r, "qbittorrent")):0.0}")} | " +
                $"{nativeMs / 1000:0.0} / {qbitMs / 1000:0.0} |");
        }

        Console.WriteLine();
        Console.WriteLine("Gate (06 §4, as revised in 08 §Q5): native good ≥ 90% of qBittorrent on ≥ 90% of evaluated searches,");
        Console.WriteLine("#1 not worse on ≥ 95%, median native time ≤ 15 s. Snapshot searches are not evaluated (show-level).");
        return 0;
    }
}
