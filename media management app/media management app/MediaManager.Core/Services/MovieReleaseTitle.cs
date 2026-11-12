using System.Text.RegularExpressions;

namespace media_management_app.Services;

/// <summary>
/// The title part of a movie release name and a strict comparison with the tracked title (audit 07c NS-01/02).
/// Used when the release year does not confirm the film (one year off, or no year at all): then a loose
/// "two title words" match accepts the neighbouring film of a franchise ("Kill Bill Vol 1" for Vol 2,
/// "Dune Part Two" for "Dune"), so the release title has to match one title variant completely and must not
/// carry a sequel marker (part/volume/number) the variant does not have.
/// </summary>
public static partial class MovieReleaseTitle
{
    private static readonly HashSet<string> OptionalTitleWords = new(StringComparer.Ordinal) { "the", "a", "an", "and" };

    private static readonly HashSet<string> SequelWords = new(StringComparer.Ordinal)
    {
        "part", "pt", "chapter", "vol", "volume", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"
    };

    // Words a release adds to a title without making it another film (strict mode only).
    private static readonly HashSet<string> EditionWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "movie", "film", "extended", "remastered", "remaster", "restored", "unrated", "uncut",
        "directors", "director", "cut", "theatrical", "imax", "edition", "special", "collectors", "criterion",
        "proper", "repack", "internal", "limited", "complete", "hybrid", "dc", "se", "version", "anniversary",
        "ultimate", "final", "open", "matte", "colorized", "gekijouban", "gekijoban"
    };

    /// <summary>
    /// The title regions of a release name: for each name separated by " | " or " / " (Nyaa "English | Romaji"),
    /// the normalized tokens before the first year (that is not part of a title), bracket, or quality/source/
    /// codec/audio marker. A leading "[Group]" is skipped.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Regions(string? releaseName, IEnumerable<string> titleVariants)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return [];
        }

        var titleTokens = titleVariants.SelectMany(TitleNormalizer.MatchTokens).ToHashSet(StringComparer.Ordinal);
        var name = LeadingGroup().Replace(releaseName, string.Empty);
        var bracket = name.IndexOfAny(['[', '(', '【', '{']);
        if (bracket > 0)
        {
            name = name[..bracket];
        }

        var regions = new List<IReadOnlyList<string>>();
        foreach (var segment in NameSeparator().Split(name))
        {
            var region = new List<string>();
            foreach (var token in TitleNormalizer.MatchTokens(segment))
            {
                var isYear = token.Length == 4 && (token.StartsWith("19", StringComparison.Ordinal) || token.StartsWith("20", StringComparison.Ordinal)) && token.All(char.IsDigit);
                if ((isYear && !titleTokens.Contains(token)) || ReleaseMarker().IsMatch(token))
                {
                    break;
                }

                region.Add(token);
            }

            if (region.Count > 0)
            {
                regions.Add(region);
            }
        }

        return regions;
    }

    /// <summary>
    /// True when one title region of <paramref name="releaseName"/> contains every word of one title variant
    /// ("the/a/an/and" optional) and no sequel marker (number, roman numeral, part/volume/"two"...) that the variant
    /// lacks. <paramref name="strict"/> (release without any year) also refuses other extra words, except edition
    /// words ("Extended", "Remastered", "The Movie"...): "Frozen Fever" is not "Frozen".
    /// </summary>
    public static bool MatchesTitle(string? releaseName, IReadOnlyList<string> titleVariants, bool strict)
    {
        var regions = Regions(releaseName, titleVariants);
        foreach (var variant in titleVariants.Where(title => !string.IsNullOrWhiteSpace(title)))
        {
            var variantTokens = TitleNormalizer.MatchTokens(variant);
            var required = variantTokens.Where(token => !OptionalTitleWords.Contains(token)).Distinct(StringComparer.Ordinal).ToList();
            if (required.Count == 0)
            {
                continue;
            }

            var variantSet = variantTokens.ToHashSet(StringComparer.Ordinal);
            foreach (var region in regions)
            {
                var regionSet = region.ToHashSet(StringComparer.Ordinal);
                if (!required.All(regionSet.Contains))
                {
                    continue;
                }

                var extras = region.Where(token => !variantSet.Contains(token)).ToList();
                if (extras.Any(IsSequelMarker))
                {
                    continue;
                }

                if (strict && extras.Any(token => !EditionWords.Contains(token)))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool IsSequelMarker(string token) => token.All(char.IsDigit) || SequelWords.Contains(token);

    [GeneratedRegex(@"^\s*(?:(?:\[[^\]]*\]|\([^)]*\)|【[^】]*】)\s*)+")]
    private static partial Regex LeadingGroup();

    [GeneratedRegex(@"\s+[|/｜]\s+")]
    private static partial Regex NameSeparator();

    [GeneratedRegex(@"^(\d{3,4}[pi]|4k|8k|uhd|hdr|hdr10|hdr10plus|dv|dovi|sdr|bluray|blu|bdrip|brrip|bdremux|bd|bdmv|remux|web|webrip|webdl|hdtv|hdrip|dvdrip|dvd|dvdscr|x264|x265|h264|h265|hevc|avc|av1|xvid|divx|10bit|8bit|aac\d*|ac3|eac3|dd\d*|ddp\d*|dts|dtshd|truehd|atmos|flac|opus|mp3|nf|amzn|dsnp|hmax|atvp)$")]
    private static partial Regex ReleaseMarker();
}
