using System.Text.RegularExpressions;

namespace media_management_app.Services;

/// <summary>
/// Short, human-readable encode attributes parsed from a release name, for comparing many encodes of the
/// same movie (native-search P5, SRCH-15): source, codec, HDR/DV, audio, bit depth and release group.
/// </summary>
public static partial class ReleaseAttributes
{
    public static IReadOnlyList<string> Describe(string? releaseName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return [];
        }

        var name = releaseName;
        var tags = new List<string>();
        AddFirst(tags, name, (RemuxRegex(), "Remux"), (BluRayRegex(), "BluRay"), (WebDlRegex(), "WEB-DL"), (WebRipRegex(), "WEBRip"),
            (HdtvRegex(), "HDTV"), (DvdRegex(), "DVD"));
        AddFirst(tags, name, (Av1Regex(), "AV1"), (X265Regex(), "x265"), (X264Regex(), "x264"));
        if (DolbyVisionRegex().IsMatch(name))
        {
            tags.Add("DV");
        }

        if (HdrRegex().Match(name) is { Success: true } hdr)
        {
            tags.Add(hdr.Value.ToUpperInvariant().Replace("PLUS", "+"));
        }

        if (TenBitRegex().IsMatch(name))
        {
            tags.Add("10bit");
        }

        AddFirst(tags, name, (AtmosRegex(), "Atmos"), (TrueHdRegex(), "TrueHD"), (DtsHdRegex(), "DTS-HD"), (DtsRegex(), "DTS"),
            (DdpRegex(), "DD+"), (Ac3Regex(), "AC3"), (FlacRegex(), "FLAC"), (AacRegex(), "AAC"), (OpusRegex(), "Opus"));
        if (GroupRegex().Match(name) is { Success: true } group)
        {
            tags.Add(group.Groups["group"].Value);
        }

        return tags;
    }

    private static void AddFirst(List<string> tags, string name, params (Regex Regex, string Tag)[] options)
    {
        foreach (var (regex, tag) in options)
        {
            if (regex.IsMatch(name))
            {
                tags.Add(tag);
                return;
            }
        }
    }

    [GeneratedRegex(@"\bremux\b", RegexOptions.IgnoreCase)] private static partial Regex RemuxRegex();
    [GeneratedRegex(@"\b(blu-?ray|bdrip|brrip|bd(?:25|50|mv)?)\b", RegexOptions.IgnoreCase)] private static partial Regex BluRayRegex();
    [GeneratedRegex(@"\bweb-?dl\b", RegexOptions.IgnoreCase)] private static partial Regex WebDlRegex();
    [GeneratedRegex(@"\bweb-?rip\b|\bweb\b", RegexOptions.IgnoreCase)] private static partial Regex WebRipRegex();
    [GeneratedRegex(@"\bhdtv\b", RegexOptions.IgnoreCase)] private static partial Regex HdtvRegex();
    [GeneratedRegex(@"\b(dvd(rip|9|5)?)\b", RegexOptions.IgnoreCase)] private static partial Regex DvdRegex();
    [GeneratedRegex(@"\bav1\b", RegexOptions.IgnoreCase)] private static partial Regex Av1Regex();
    [GeneratedRegex(@"\b(x265|h\.?265|hevc)\b", RegexOptions.IgnoreCase)] private static partial Regex X265Regex();
    [GeneratedRegex(@"\b(x264|h\.?264|avc)\b", RegexOptions.IgnoreCase)] private static partial Regex X264Regex();
    [GeneratedRegex(@"\b(dv|dovi|dolby\.?vision)\b", RegexOptions.IgnoreCase)] private static partial Regex DolbyVisionRegex();
    [GeneratedRegex(@"\bhdr10(?:\+|plus)?(?!\w)|\bhdr\b|\bhlg\b", RegexOptions.IgnoreCase)] private static partial Regex HdrRegex();
    [GeneratedRegex(@"\b10-?bit\b", RegexOptions.IgnoreCase)] private static partial Regex TenBitRegex();
    [GeneratedRegex(@"\batmos\b", RegexOptions.IgnoreCase)] private static partial Regex AtmosRegex();
    [GeneratedRegex(@"\btrue-?hd\b", RegexOptions.IgnoreCase)] private static partial Regex TrueHdRegex();
    [GeneratedRegex(@"\bdts-?hd\b|\bdts-?x\b", RegexOptions.IgnoreCase)] private static partial Regex DtsHdRegex();
    [GeneratedRegex(@"\bdts\b", RegexOptions.IgnoreCase)] private static partial Regex DtsRegex();
    [GeneratedRegex(@"\b(ddp|dd\+|e-?ac-?3)", RegexOptions.IgnoreCase)] private static partial Regex DdpRegex();
    [GeneratedRegex(@"\b(ac-?3|dd5|dd2)", RegexOptions.IgnoreCase)] private static partial Regex Ac3Regex();
    [GeneratedRegex(@"\bflac\b", RegexOptions.IgnoreCase)] private static partial Regex FlacRegex();
    [GeneratedRegex(@"\baac", RegexOptions.IgnoreCase)] private static partial Regex AacRegex();
    [GeneratedRegex(@"\bopus\b", RegexOptions.IgnoreCase)] private static partial Regex OpusRegex();
    // "WEB-DL", "Blu-Ray", "DTS-HD" at the end of a name are tags, not the release group (07c NS-30).
    [GeneratedRegex(@"(?<!\b(?:web|blu|dts|hdr10|ac|e))-(?<group>(?!(?:dl|rip|ray|hd|x|ma)\b)[A-Za-z0-9]{2,15})(\.(mkv|mp4|avi))?\s*$|^\[(?<group>[^\]]{2,30})\]", RegexOptions.IgnoreCase)] private static partial Regex GroupRegex();
}
