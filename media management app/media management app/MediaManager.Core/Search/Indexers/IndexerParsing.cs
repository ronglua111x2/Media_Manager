using System.Globalization;
using System.Text.RegularExpressions;

namespace media_management_app.Search.Indexers;

internal static class IndexerParsing
{
    /// <summary>
    /// The response had result items but none could be read (a field was renamed, the layout changed): that is a
    /// parse error for the breaker, not "no results" (audit 07c NS-13).
    /// </summary>
    public static void ThrowIfUnreadable(int parsed, int unreadable, string indexerId)
    {
        if (parsed == 0 && unreadable > 0)
        {
            throw new FormatException($"{indexerId}: {unreadable} result item(s) but none could be read (format change?).");
        }
    }

    private const long MaxUnixSeconds = 253_402_300_799;

    /// <summary>Unix seconds (or milliseconds, when the value is too large for seconds) as a date; null when out of range.</summary>
    public static DateTimeOffset? FromUnix(long value)
    {
        if (value <= 0)
        {
            return null;
        }

        if (value > MaxUnixSeconds)
        {
            value /= 1000;
        }

        return value is > 0 and <= MaxUnixSeconds ? DateTimeOffset.FromUnixTimeSeconds(value) : null;
    }

    /// <summary>Unix milliseconds as a date; null when out of range.</summary>
    public static DateTimeOffset? FromUnixMilliseconds(long value) =>
        value is > 0 and <= (MaxUnixSeconds * 1000) + 999 ? DateTimeOffset.FromUnixTimeMilliseconds(value) : null;

    private static readonly Regex SizeRegex = new(@"(?<value>[\d.,]+)\s*(?<unit>[KMGT]i?B|B|bytes)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"1.24 GB", "430.5 MiB", "700 MB" → bytes (binary multiples, as torrent sites use them).</summary>
    public static long ParseSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var match = SizeRegex.Match(text);
        if (!match.Success ||
            !double.TryParse(match.Groups["value"].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        var multiplier = char.ToUpperInvariant(match.Groups["unit"].Value[0]) switch
        {
            'K' => 1024d,
            'M' => 1024d * 1024,
            'G' => 1024d * 1024 * 1024,
            'T' => 1024d * 1024 * 1024 * 1024,
            _ => 1d
        };
        return (long)(value * multiplier);
    }

    public static int ParseInt(string? text) =>
        int.TryParse((text ?? string.Empty).Replace(",", "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public static long ParseLong(string? text) =>
        long.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public static string Decode(string? html) =>
        System.Net.WebUtility.HtmlDecode(html ?? string.Empty).Trim();
}
