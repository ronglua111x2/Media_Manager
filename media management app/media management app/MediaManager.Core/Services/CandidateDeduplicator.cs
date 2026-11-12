using System.Text;
using System.Text.RegularExpressions;

namespace media_management_app.Services;

/// <summary>
/// Removes duplicate search results (recipe "Deduplicate candidates" / "Fuzzy deduplicate").
/// Search engines already report the size, so no metadata probe is needed.
///
/// 1. Exact: the same torrent is one candidate. Identity is the info-hash when the link is a magnet
///    (the same release from two engines has two different magnet URLs — e.g. the piratebay and
///    thepiratebay plugins return identical rows), otherwise the URL.
/// 2. Fuzzy (optional): same normalized release name and sizes within the tolerance. Separators
///    ("." "_" "-" brackets) are ignored and CRC tags like [0FBE586A] dropped, but the release group
///    is kept, so two groups' releases are never merged. Sizes are compared pairwise within the
///    tolerance (no fixed buckets, so 4.99 MB and 5.01 MB still merge with a 5 MB tolerance).
/// The kept entry is the one with the most seeders.
/// </summary>
public static class CandidateDeduplicator
{
    private static readonly Regex Btih = new(@"xt=urn:btih:(?<hash>[a-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CrcTag = new(@"[\[(][0-9a-f]{8}[\])]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VideoExtension = new(@"\.(mkv|mp4|avi|m4v|ts|wmv)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Separators = new(@"[\s._\-\[\]()]+", RegexOptions.Compiled);

    /// <summary>Lower-case 40-char hex info-hash from a magnet link (hex or base32 form), or null.</summary>
    public static string? TryGetInfoHash(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = Btih.Match(url);
        if (!match.Success)
        {
            return null;
        }

        var hash = match.Groups["hash"].Value;
        if (hash.Length == 40 && hash.All(Uri.IsHexDigit))
        {
            return hash.ToLowerInvariant();
        }

        return hash.Length == 32 ? Base32ToHex(hash) : null;
    }

    /// <summary>Identity of one torrent: info-hash when known, else the URL.</summary>
    public static string IdentityKey(string? url) =>
        TryGetInfoHash(url) is { } hash ? "btih:" + hash : "url:" + (url ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Release name normalized for fuzzy comparison (keeps the group, drops separators and CRC tags).</summary>
    public static string NormalizeReleaseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var value = VideoExtension.Replace(name.Trim(), string.Empty);
        value = CrcTag.Replace(value, " ");
        value = TitleNormalizer.FoldDiacritics(value).ToLowerInvariant();
        return Separators.Replace(value, " ").Trim();
    }

    public static List<T> Deduplicate<T>(
        IEnumerable<T> items,
        Func<T, string?> name,
        Func<T, long> size,
        Func<T, string?> url,
        Func<T, int> seeders,
        bool fuzzy,
        int sizeToleranceMb)
    {
        // Keep first-seen order; within a duplicate group keep the best-seeded entry.
        var exact = items
            .Select((item, index) => (Item: item, Index: index))
            .GroupBy(entry => IdentityKey(url(entry.Item)), StringComparer.Ordinal)
            .Select(group => (
                Item: group.OrderByDescending(entry => seeders(entry.Item)).ThenBy(entry => entry.Index).First().Item,
                Index: group.Min(entry => entry.Index)))
            .OrderBy(entry => entry.Index)
            .ToList();

        if (!fuzzy)
        {
            return exact.Select(entry => entry.Item).ToList();
        }

        var tolerance = Math.Max(0, sizeToleranceMb) * 1024L * 1024L;
        var kept = new List<(T Item, int Index, string Name, long Size)>();
        foreach (var entry in exact)
        {
            var key = NormalizeReleaseName(name(entry.Item));
            var itemSize = size(entry.Item);
            var duplicateIndex = kept.FindIndex(existing =>
                key.Length > 0 &&
                existing.Name == key &&
                SameSize(existing.Size, itemSize, tolerance));
            if (duplicateIndex < 0)
            {
                kept.Add((entry.Item, entry.Index, key, itemSize));
                continue;
            }

            if (seeders(entry.Item) > seeders(kept[duplicateIndex].Item))
            {
                kept[duplicateIndex] = (entry.Item, kept[duplicateIndex].Index, key, itemSize);
            }
        }

        return kept.OrderBy(entry => entry.Index).Select(entry => entry.Item).ToList();
    }

    private static bool SameSize(long left, long right, long tolerance)
    {
        // Unknown sizes (0) only merge with other unknown sizes.
        if (left <= 0 || right <= 0)
        {
            return left <= 0 && right <= 0;
        }

        return Math.Abs(left - right) <= tolerance;
    }

    private static string? Base32ToHex(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var bytes = new List<byte>(20);
        foreach (var character in base32.ToUpperInvariant())
        {
            var index = alphabet.IndexOf(character);
            if (index < 0)
            {
                return null;
            }

            value = (value << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        if (bytes.Count != 20)
        {
            return null;
        }

        var builder = new StringBuilder(40);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }
}
