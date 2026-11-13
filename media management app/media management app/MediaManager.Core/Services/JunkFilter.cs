using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// Rejects obvious junk before any recipe scoring (native-search P5, docs/planning/native-search/05 §3):
/// executables/installers dressed as media, "codec pack"/keygen bait, password-protected archive bait and
/// sizes that cannot hold the claimed quality. Thresholds are deliberately conservative (short anime
/// episodes and HEVC encodes are small); anything borderline is left to the recipe's own size filter.
/// </summary>
public static partial class JunkFilter
{
    private const long Mb = 1024L * 1024L;

    public static string? GetJunkReason(SearchRecipe recipe, TorrentSearchResult result)
    {
        var name = result.FileName ?? string.Empty;
        // Resolution from standalone tokens only: the substring detection used for quality filters reads
        // "4K.Remastered.1080p" and "384kbps" as 2160p, which made normal 1080p/720p sizes look implausible.
        var detectedQuality = ResolutionFromTokens(name);
        if (ExecutableRegex().IsMatch(name))
        {
            return "name looks like an executable or installer";
        }

        if (BaitRegex().IsMatch(name))
        {
            return "codec pack / keygen / password bait";
        }

        if (result.FileSize > 0 && MinimumPlausibleSize(recipe.TargetKind, detectedQuality) is { } minimum && result.FileSize < minimum)
        {
            return $"{result.FileSize / Mb} MB is implausibly small for {detectedQuality} {recipe.TargetKind}";
        }

        return null;
    }

    /// <summary>
    /// The resolution a release claims, read from standalone tokens: "1080p", "1920x1080", then (only without
    /// those) "FHD"/bare "1080", then "4K"/"UHD" marketing words. So "4K Remastered 1080p" is 1080p and "384kbps" is
    /// nothing. With several resolutions the highest claim counts (a fake listing "2160p 1080p 720p" is judged as
    /// 2160p). Null when the name has none.
    /// </summary>
    public static string? ResolutionFromTokens(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var tokens = TorrentCandidateParser.Tokenize(name).Select(token => token.ToLowerInvariant()).ToList();
        int? pixels = null;
        foreach (var token in tokens)
        {
            int? value = token switch
            {
                "2160p" => 2160,
                "1440p" => 1440,
                "1080p" or "1080i" => 1080,
                "720p" => 720,
                _ => FrameSize().Match(token) is { Success: true } frame ? int.Parse(frame.Groups["h"].Value) : null
            };
            if (value is not null && (pixels is null || value > pixels))
            {
                pixels = value;
            }
        }

        pixels ??= tokens
            .Select(token => token switch
            {
                "fhd" or "1080" => 1080,
                "2160" => 2160,
                "1440" => 1440,
                "720" => 720,
                _ => (int?)null
            })
            .Max();

        if (pixels is null && tokens.Any(token => token is "4k" or "uhd"))
        {
            pixels = 2160;
        }

        pixels = pixels switch
        {
            >= 2160 => 2160,
            >= 1440 => 1440,
            >= 1080 => 1080,
            >= 720 => 720,
            _ => null
        };

        return pixels switch
        {
            2160 => "2160p",
            1440 => "1440p",
            1080 => "1080p",
            720 => "720p",
            _ => null
        };
    }

    private static long? MinimumPlausibleSize(MediaKind kind, string? quality)
    {
        var rank = (quality ?? string.Empty).ToLowerInvariant() switch
        {
            "2160p" => 3,
            "1440p" or "1080p" => 2,
            "720p" => 1,
            _ => 0
        };
        if (rank == 0)
        {
            return null;
        }

        return kind switch
        {
            MediaKind.Movie => rank switch { 3 => 1024 * Mb, 2 => 300 * Mb, _ => 150 * Mb },
            MediaKind.TvSeasonPack => rank switch { 3 => 500 * Mb, 2 => 200 * Mb, _ => 100 * Mb },
            _ => rank switch { 3 => 60 * Mb, 2 => 25 * Mb, _ => 12 * Mb }
        };
    }

    [GeneratedRegex(@"^\d{3,4}x(?<h>\d{3,4})$")]
    private static partial Regex FrameSize();

    // exe/msi/lnk/vbs/apk never occur as words in titles; scr/bat/cmd do (".SCR." screener tag, "The.Bat.1959"),
    // so those only count as the final extension of the name. Some sources (qBittorrent's torrentdownloads plugin)
    // turn the dots into spaces: "Rick and Morty S09E10 1080p WEB h264-EDITH scr" is the same fake (bench 13/11/2026).
    [GeneratedRegex(@"\.(exe|msi|lnk|vbs|apk)\b|[.\s](exe|msi|lnk|vbs|apk|scr|bat|cmd)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ExecutableRegex();

    [GeneratedRegex(@"\bcodec\s*pack\b|\bkeygen\b|\bpassword\s*(protected|\.txt|in\s+(comments|description|file))", RegexOptions.IgnoreCase)]
    private static partial Regex BaitRegex();
}
