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

    public static string? GetJunkReason(SearchRecipe recipe, TorrentSearchResult result, string? detectedQuality)
    {
        var name = result.FileName ?? string.Empty;
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

    [GeneratedRegex(@"\.(exe|scr|bat|cmd|msi|lnk|vbs|apk)(\b|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ExecutableRegex();

    [GeneratedRegex(@"\bcodec\s*pack\b|\bkeygen\b|\bpassword\s*(protected|\.txt|in\s+(comments|description|file))", RegexOptions.IgnoreCase)]
    private static partial Regex BaitRegex();
}
