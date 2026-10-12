namespace media_management_app.Services;

public static class TorrentQuality
{
    private static readonly string[] RankedQualities = ["2160p", "1440p", "1080p", "720p", "480p"];

    private static readonly IReadOnlyDictionary<string, string[]> QualityAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["2160p"] = ["2160p", "2160", "4k", "uhd", "ultra hd", "ultrahd"],
            ["1440p"] = ["1440p", "1440", "qhd"],
            ["1080p"] = ["1080p", "1080", "full hd", "fullhd", "fhd"],
            ["720p"] = ["720p", "720"],
            ["480p"] = ["480p", "480"]
        };

    public static IReadOnlyList<string> AllQualities => RankedQualities;

    public static string Detect(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        foreach (var quality in RankedQualities)
        {
            if (!QualityAliases.TryGetValue(quality, out var aliases))
            {
                continue;
            }

            if (aliases.Any(alias => fileName.Contains(alias, StringComparison.OrdinalIgnoreCase)))
            {
                return quality;
            }
        }

        return string.Empty;
    }

    public static int GetRank(string quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
        {
            return 0;
        }

        var index = Array.FindIndex(RankedQualities, rankedQuality =>
            string.Equals(rankedQuality, quality, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : RankedQualities.Length - index;
    }

    public static bool MatchesSelectedQuality(string quality, IReadOnlyList<string> selectedQualities)
    {
        if (selectedQualities.Count == 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(quality) &&
               selectedQualities.Any(selectedQuality =>
                   string.Equals(selectedQuality, quality, StringComparison.OrdinalIgnoreCase));
    }
}
