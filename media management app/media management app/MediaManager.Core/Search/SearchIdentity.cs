using System.Text.RegularExpressions;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.Search;

/// <summary>
/// Identity of a search result across backends. The qBittorrent "thepiratebay" plugin returns the TPB
/// description page (description.php?id=…) instead of a magnet, while the native apibay indexer returns the
/// info-hash plus that same description page. Comparing by URL counted one torrent twice and reported it as
/// "only found by qBittorrent". Native rows teach the alias description-id → info-hash.
/// </summary>
public static partial class SearchIdentity
{
    public static string? TpbDescriptionId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var decoded = Uri.UnescapeDataString(url);
        var match = TpbDescriptionRegex().Match(decoded);
        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>Alias map (tpb description id → identity key) learned from rows that carry both.</summary>
    public static Dictionary<string, string> BuildAliases(IEnumerable<TorrentSearchResult> rows)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = CandidateDeduplicator.IdentityKey(row.FileUrl);
            if (!key.StartsWith("btih:", StringComparison.Ordinal))
            {
                continue;
            }

            if (TpbDescriptionId(row.DescriptionUrl) is { } id)
            {
                aliases.TryAdd("tpb:" + id, key);
            }
        }

        return aliases;
    }

    public static string Key(TorrentSearchResult row, IReadOnlyDictionary<string, string> aliases)
    {
        var key = CandidateDeduplicator.IdentityKey(row.FileUrl);
        if (key.StartsWith("btih:", StringComparison.Ordinal))
        {
            return key;
        }

        var id = TpbDescriptionId(row.FileUrl) ?? TpbDescriptionId(row.DescriptionUrl);
        return id is not null && aliases.TryGetValue("tpb:" + id, out var alias) ? alias : key;
    }

    // thepiratebay.org/description.php?id=… and Knaben's knaben.xyz/thepiratebay/description.php?id=…
    [GeneratedRegex(@"thepiratebay(?:\.[a-z]+)?/description\.php\?id=(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TpbDescriptionRegex();
}
