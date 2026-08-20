using System.Text.RegularExpressions;
using media_management_app.Models;

namespace media_management_app.Common;

/// <summary>
/// Listing identity for blacklist checks at search/rank time (no network).
/// Magnet URLs expose infohash; HTML details pages use the URL itself.
/// </summary>
public static partial class TorrentListingIdentity
{
    private static readonly Regex BtihRegex = BtihPattern();

    public static string? TryParseInfoHashFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var normalized = TorrentSearchResult.NormalizeUrl(url);
        var match = BtihRegex.Match(normalized);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups[1].Value;
        return NormalizeInfoHash(raw);
    }

    public static string NormalizeListingUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        return TorrentSearchResult.NormalizeUrl(url);
    }

    public static string? NormalizeInfoHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        var trimmed = hash.Trim();
        if (trimmed.Length == 40 && trimmed.All(IsHex))
        {
            return trimmed.ToLowerInvariant();
        }

        if (trimmed.Length == 32)
        {
            try
            {
                var bytes = Base32Decode(trimmed.ToUpperInvariant());
                if (bytes.Length == 20)
                {
                    return Convert.ToHexString(bytes).ToLowerInvariant();
                }
            }
            catch
            {
                // Fall through
            }
        }

        return trimmed.ToLowerInvariant();
    }

    private static bool IsHex(char c) =>
        c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>(input.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var c in input)
        {
            var value = alphabet.IndexOf(c);
            if (value < 0)
            {
                continue;
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return output.ToArray();
    }

    [GeneratedRegex(@"xt=urn:btih:([a-zA-Z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BtihPattern();
}
