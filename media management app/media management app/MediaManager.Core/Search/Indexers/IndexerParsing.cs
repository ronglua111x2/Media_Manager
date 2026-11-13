using System.Globalization;
using System.Text.RegularExpressions;

namespace media_management_app.Search.Indexers;

internal static class IndexerParsing
{
    /// <summary>
    /// A further page of results, or null when the per-search request cap (07d GOV-03 B) stops the paging: the pages
    /// already read are kept. The first page is never swallowed: a capped first request trims the whole query.
    /// </summary>
    public static async Task<IndexerHttpResponse?> NextPageAsync(Func<Task<IndexerHttpResponse>> fetch, bool firstPage)
    {
        try
        {
            return await fetch();
        }
        catch (IndexerSearchCapException) when (!firstPage)
        {
            return null;
        }
    }

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

    private static readonly Regex DecimalComma = new(@"^\d+,\d{1,2}$", RegexOptions.Compiled);

    private static readonly Regex SizeRegex = new(@"(?<value>[\d.,]+)\s*(?<unit>[KMGT]i?B|B|bytes)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// "1.24 GB", "430.5 MiB", "700 MB", "1,024.5 MB", "1,5 GB" → bytes (binary multiples, as torrent sites use them).
    /// A comma followed by one or two final digits is a decimal comma ("1,5 GB" is 1.5 GB, not 15 GB, 07c NS-30);
    /// otherwise commas separate thousands.
    /// </summary>
    public static long ParseSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var match = SizeRegex.Match(text);
        if (!match.Success)
        {
            return 0;
        }

        var number = match.Groups["value"].Value.TrimEnd('.', ',');
        number = !number.Contains('.') && DecimalComma.IsMatch(number) ? number.Replace(',', '.') : number.Replace(",", "");
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
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
