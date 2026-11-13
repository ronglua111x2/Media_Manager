using System.Text.RegularExpressions;
using media_management_app.Models;

namespace media_management_app.Services;

public static class PackSpecialPatternInferrer
{
    private static readonly Regex OvaDashNumberRegex = new(
        @"\bOVA\s*-\s*(?<index>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // S00E05, S0E5, S00E05v2, and "_S00E05_" (a .NET \b finds no boundary between "_" and "S") (audit L2-02).
    private static readonly Regex StandardS00ERegex = new(
        @"(?<![A-Za-z0-9])S0{1,3}E(?<episode>\d{1,4})(?:v\d{1,2})?(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static InferredSpecialPattern Infer(
        IReadOnlyList<string> stems,
        int? expectedSpecialCount = null)
    {
        if (stems.Count == 0)
        {
            return InferredSpecialPattern.Invalid;
        }

        if (TryBuildOvaDashPattern(stems, out var ovaPattern))
        {
            if (expectedSpecialCount is not > 0 || ValidateOvaDashPattern(ovaPattern, stems, expectedSpecialCount.Value))
            {
                return ovaPattern;
            }
        }

        if (TryBuildStandardS00Pattern(stems, out var s00Pattern))
        {
            if (expectedSpecialCount is not > 0 || ValidateStandardS00Pattern(s00Pattern, stems, expectedSpecialCount.Value))
            {
                return s00Pattern;
            }
        }

        var fixedWidth = PackEpisodePatternInferrer.Infer(stems, expectedSpecialCount);
        if (!fixedWidth.IsValid)
        {
            return InferredSpecialPattern.Invalid;
        }

        return new InferredSpecialPattern
        {
            Style = InferredSpecialNamingStyle.FixedWidthNumber,
            EpisodePattern = fixedWidth,
            IsValid = true
        };
    }

    public static int? TryExtractIndex(string stem, InferredSpecialPattern pattern)
    {
        if (!pattern.IsValid || string.IsNullOrWhiteSpace(stem))
        {
            return null;
        }

        return pattern.Style switch
        {
            InferredSpecialNamingStyle.OvaDashNumber => TryExtractOvaDashIndex(stem),
            InferredSpecialNamingStyle.StandardS00E => TryExtractStandardS00Index(stem),
            InferredSpecialNamingStyle.FixedWidthNumber => pattern.EpisodePattern.TryExtractFixedWidth(stem),
            _ => null
        };
    }

    private static bool TryBuildOvaDashPattern(IReadOnlyList<string> stems, out InferredSpecialPattern pattern)
    {
        pattern = InferredSpecialPattern.Invalid;
        var matched = stems.Count(stem => TryExtractOvaDashIndex(stem) is not null);
        if (matched < Math.Max(1, stems.Count / 2))
        {
            return false;
        }

        pattern = new InferredSpecialPattern
        {
            Style = InferredSpecialNamingStyle.OvaDashNumber,
            IsValid = true
        };
        return true;
    }

    private static bool TryBuildStandardS00Pattern(IReadOnlyList<string> stems, out InferredSpecialPattern pattern)
    {
        pattern = InferredSpecialPattern.Invalid;
        var matched = stems.Count(stem => TryExtractStandardS00Index(stem) is not null);
        if (matched < Math.Max(1, stems.Count / 2))
        {
            return false;
        }

        pattern = new InferredSpecialPattern
        {
            Style = InferredSpecialNamingStyle.StandardS00E,
            IsValid = true
        };
        return true;
    }

    private static bool ValidateOvaDashPattern(
        InferredSpecialPattern pattern,
        IReadOnlyList<string> stems,
        int expectedSpecialCount)
    {
        var extracted = stems
            .Select(stem => TryExtractIndex(stem, pattern))
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .Distinct()
            .ToList();

        return extracted.Count >= Math.Min(stems.Count, expectedSpecialCount) / 2;
    }

    private static bool ValidateStandardS00Pattern(
        InferredSpecialPattern pattern,
        IReadOnlyList<string> stems,
        int expectedSpecialCount)
    {
        var extracted = stems
            .Select(stem => TryExtractIndex(stem, pattern))
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .Distinct()
            .ToList();

        return extracted.Count >= Math.Min(stems.Count, expectedSpecialCount) / 2;
    }

    private static int? TryExtractOvaDashIndex(string stem)
    {
        var match = OvaDashNumberRegex.Match(stem);
        return match.Success && int.TryParse(match.Groups["index"].Value, out var index)
            ? index
            : null;
    }

    /// <summary>The Season 0 episode a <c>S00Exx</c> file name states, or null.</summary>
    public static int? TryExtractStandardS00Index(string stem)
    {
        var match = StandardS00ERegex.Match(stem);
        return match.Success && int.TryParse(match.Groups["episode"].Value, out var episode)
            ? episode
            : null;
    }
}
