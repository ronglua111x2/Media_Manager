using System.Globalization;
using System.Text.Json;
using MediaManager.App.Tests.Special.Fixtures;
using media_management_app.Models;

namespace GeminiSpecialEval.Cases;

/// <summary>Every case the harness knows: the test fixtures, the eval-only synthetic cases and private JSON cases.</summary>
internal static class CaseCatalog
{
    /// <summary>Tags for the test fixtures (the fixtures themselves carry none).</summary>
    private static readonly Dictionary<string, string[]> FixtureTags = new(StringComparer.Ordinal)
    {
        ["brass-two-season-pack"] = ["season", "real-shape"],
        ["rail-untitled"] = ["season", "real-shape", "recap-gap"],
        ["rail-titled"] = ["season", "real-shape", "recap-gap"],
        ["cafe-bd-shorts"] = ["season", "real-shape"],
        ["cafe-later-shorts"] = ["opaque", "real-shape"],
        ["rooftop-complete-minis"] = ["season", "real-shape", "large"],
        ["archive-s1-ovas"] = ["season", "real-shape"],
        ["club-ova-per-season"] = ["season", "real-shape"],
        ["recap-before-season-one"] = ["season", "recap-gap"]
    };

    public static IReadOnlyList<EvalCase> Synthetic()
    {
        var fixtures = SpecialCases.Generalisation()
            .Append(SpecialCases.RecapBeforeSeasonOne())
            .Select(fixture => EvalCase.FromFixture(
                fixture,
                CaseSources.Fixtures,
                FixtureTags.GetValueOrDefault(fixture.Name) ?? []));
        return fixtures.Concat(EvalOnlyCases.All()).ToList();
    }

    /// <summary>
    /// Private cases: <c>cases/*.json</c> in the data folder. A case is used only when its ground truth was
    /// confirmed by the user (<c>groundTruthConfirmedByUser</c>), unless <paramref name="includeUnconfirmed"/>.
    /// </summary>
    public static IReadOnlyList<EvalCase> Private(string? dataDir, bool includeUnconfirmed, List<string> notes)
    {
        if (dataDir is null)
        {
            return [];
        }

        var folder = Path.Combine(dataDir, "cases");
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var cases = new List<EvalCase>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var file = JsonSerializer.Deserialize<PrivateCaseFile>(File.ReadAllText(path), Json.ReadOptions)
                           ?? throw new InvalidDataException("empty file");
                if (!file.GroundTruthConfirmedByUser && !includeUnconfirmed)
                {
                    notes.Add($"{Path.GetFileName(path)}: skipped, ground truth not confirmed by the user.");
                    continue;
                }

                cases.Add(file.ToCase());
            }
            catch (Exception ex)
            {
                notes.Add($"{Path.GetFileName(path)}: not loaded ({ex.Message}).");
            }
        }

        return cases;
    }

    public static IReadOnlyList<EvalCase> Select(IReadOnlyList<EvalCase> all, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || filter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return all;
        }

        var selected = new List<EvalCase>();
        foreach (var token in filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            IEnumerable<EvalCase> matches = token.StartsWith("tag:", StringComparison.OrdinalIgnoreCase)
                ? all.Where(item => item.Tags.Contains(token[4..], StringComparer.OrdinalIgnoreCase))
                : token.StartsWith("source:", StringComparison.OrdinalIgnoreCase)
                    ? all.Where(item => item.Source.Equals(token[7..], StringComparison.OrdinalIgnoreCase))
                    : all.Where(item => item.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
            var list = matches.ToList();
            if (list.Count == 0)
            {
                throw new ArgumentException($"No case matches '{token}'. Run 'list' to see the cases.");
            }

            selected.AddRange(list.Where(item => !selected.Contains(item)));
        }

        return selected;
    }

    /// <summary>
    /// JSON shape of a private case. Episodes are <c>[season, episode, title, "yyyy-MM-dd" or null]</c>; files are
    /// pack-relative paths with '/'; <c>expected</c> maps each special file to its S00 episode (null = not a special).
    /// </summary>
    private sealed class PrivateCaseFile
    {
        public string Name { get; set; } = string.Empty;

        public string ShowTitle { get; set; } = string.Empty;

        public int TmdbId { get; set; }

        public string Note { get; set; } = string.Empty;

        public string[] Tags { get; set; } = [];

        public JsonElement[][] Episodes { get; set; } = [];

        public string[] Files { get; set; } = [];

        public Dictionary<string, int?> Expected { get; set; } = [];

        public bool GroundTruthConfirmedByUser { get; set; }

        public EvalCase ToCase()
        {
            if (string.IsNullOrWhiteSpace(Name) || Files.Length == 0 || Episodes.Length == 0)
            {
                throw new InvalidDataException("name, files and episodes are required");
            }

            var episodes = Episodes
                .Select(row => SpecialShowBuilder.Episode(
                    row[0].GetInt32(),
                    row[1].GetInt32(),
                    row.Length > 2 ? row[2].GetString() ?? string.Empty : string.Empty,
                    row.Length > 3 && row[3].ValueKind == JsonValueKind.String
                        ? DateTime.ParseExact(row[3].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : null))
                .ToList();
            var files = Files
                .Select(path => path.Replace('\\', '/'))
                .Select(path => (path, path[(path.LastIndexOf('/') + 1)..]))
                .ToList();
            var expected = Expected.ToDictionary(
                pair => pair.Key.Replace('\\', '/'),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            var missing = expected.Keys.Where(path => !files.Any(file => file.Item1.Equals(path, StringComparison.OrdinalIgnoreCase))).ToList();
            if (missing.Count > 0)
            {
                throw new InvalidDataException($"expected lists {missing.Count} path(s) that are not in files");
            }

            return new EvalCase(Name, CaseSources.Private, ShowTitle, TmdbId, episodes, files, expected, Note, Tags.Append("private").ToArray());
        }
    }
}
