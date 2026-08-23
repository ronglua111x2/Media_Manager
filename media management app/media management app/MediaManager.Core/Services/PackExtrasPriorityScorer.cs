using System.Text.RegularExpressions;

namespace media_management_app.Services;

public static class PackExtrasPriorityScorer
{
    private static readonly Regex ExtrasKeywordRegex = new(
        @"\b(?:ova|oad|oav|special|extra)s?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ContainsExtrasKeywords(string? torrentName) =>
        !string.IsNullOrWhiteSpace(torrentName) && ExtrasKeywordRegex.IsMatch(torrentName);
}
