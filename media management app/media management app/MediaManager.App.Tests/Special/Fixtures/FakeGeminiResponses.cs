using System.Text;
using System.Text.Json;

namespace MediaManager.App.Tests.Special.Fixtures;

/// <summary>
/// Gemini responses for the special mapping tests. No test calls Gemini; these reproduce the response shapes
/// recorded during the parser audit (report sections 4a and 10.2, appendix A probes A and E).
/// Candidate indexes are looked up by file name in the request, so the responses stay valid if the payload order changes.
/// </summary>
internal static class FakeGeminiResponses
{
    /// <summary>
    /// The wrong answer real Gemini gave three times out of three for the two-season pack:
    /// S1S01-S1S06 one slot late (E2-E7), S1S07 on E1, the OVA on E8, S2 right (E9-E15).
    /// </summary>
    public static string RecordedSevenOfFifteenWrong(string userJson) =>
        ByFileName(userJson, fileName =>
        {
            if (fileName.Contains("S1OVA", StringComparison.OrdinalIgnoreCase))
            {
                return 8;
            }

            var index = SpecialIndex(fileName, "S1S");
            if (index is not null)
            {
                return index == 7 ? 1 : index + 1;
            }

            index = SpecialIndex(fileName, "S2S");
            return index is null ? null : index + 8;
        });

    /// <summary>The right answer for the two-season pack (what the library holds today).</summary>
    public static string CorrectTwoSeasonPack(string userJson) =>
        ByFileName(userJson, fileName =>
        {
            if (fileName.Contains("S1OVA", StringComparison.OrdinalIgnoreCase))
            {
                return 8;
            }

            var index = SpecialIndex(fileName, "S1S");
            if (index is not null)
            {
                return index;
            }

            index = SpecialIndex(fileName, "S2S");
            return index is null ? null : index + 8;
        });

    /// <summary>The correct answer plus a second, different pair for the first S1 special (same candidateIndex twice).</summary>
    public static string DuplicateCandidateIndex(string userJson)
    {
        var pairs = Pairs(userJson, fileName => SpecialIndex(fileName, "S1S") is { } index ? index : null);
        var first = pairs.First();
        pairs.Insert(1, (first.CandidateIndex, 20));
        return Serialize(pairs);
    }

    /// <summary>Two different files mapped to S00E01.</summary>
    public static string DuplicateS00E(string userJson) =>
        ByFileName(userJson, fileName => SpecialIndex(fileName, "S1S") switch
        {
            1 or 2 => 1,
            { } index => index,
            _ => null
        });

    /// <summary>A response cut in the middle of the mapping array (finishReason MAX_TOKENS).</summary>
    public const string TruncatedInMappings = "{\"m\":[[0,1],[1,2],[2,";

    /// <summary>Valid JSON without the mapping array (quota text instead of an answer).</summary>
    public const string MissingMappingArray = "{\"w\":[\"quota exceeded\"]}";

    public static string ByFileName(string userJson, Func<string, int?> s00eForFile) =>
        Serialize(Pairs(userJson, s00eForFile));

    public static IReadOnlyList<string> CandidateFileNames(string userJson)
    {
        using var document = JsonDocument.Parse(userJson);
        return document.RootElement
            .GetProperty("ctx")
            .GetProperty("c")
            .EnumerateArray()
            .Select(candidate => candidate[1].GetString() ?? string.Empty)
            .ToList();
    }

    private static List<(int CandidateIndex, int S00E)> Pairs(string userJson, Func<string, int?> s00eForFile)
    {
        var names = CandidateFileNames(userJson);
        var pairs = new List<(int, int)>();
        for (var index = 0; index < names.Count; index++)
        {
            if (s00eForFile(names[index]) is { } s00e)
            {
                pairs.Add((index, s00e));
            }
        }

        return pairs;
    }

    private static string Serialize(IEnumerable<(int CandidateIndex, int S00E)> pairs)
    {
        var builder = new StringBuilder("{\"m\":[");
        builder.AppendJoin(',', pairs.Select(pair => $"[{pair.CandidateIndex},{pair.S00E}]"));
        builder.Append("],\"w\":[]}");
        return builder.ToString();
    }

    private static int? SpecialIndex(string fileName, string marker)
    {
        var at = fileName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }

        var digits = new string(fileName[(at + marker.Length)..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : null;
    }
}
