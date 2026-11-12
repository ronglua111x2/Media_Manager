using System.Text.RegularExpressions;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.Search;

/// <summary>
/// Identity of a search result across backends. Some qBittorrent plugins return a site page instead of a
/// magnet: "thepiratebay" returns description.php?id=…, "limetorrents" returns …-torrent-&lt;id&gt;.html. The
/// native indexers return the info-hash plus that same page. Comparing by URL counted one torrent twice and
/// reported it as "only found by qBittorrent". Native rows teach the aliases (site page id → info-hash).
/// </summary>
public static partial class SearchIdentity
{
    /// <summary>Site-page aliases of a URL: "tpb:&lt;id&gt;" and/or "lime:&lt;id&gt;".</summary>
    public static IEnumerable<string> AliasesOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            yield break;
        }

        var decoded = Uri.UnescapeDataString(url);
        if (TpbDescriptionRegex().Match(decoded) is { Success: true } tpb)
        {
            yield return "tpb:" + tpb.Groups["id"].Value;
        }

        if (LimeDetailRegex().Match(decoded) is { Success: true } lime)
        {
            yield return "lime:" + lime.Groups["id"].Value;
        }
    }

    public static string? TpbDescriptionId(string? url) =>
        AliasesOf(url).FirstOrDefault(alias => alias.StartsWith("tpb:", StringComparison.Ordinal))?[4..];

    /// <summary>Alias map (site page id → identity key) learned from rows that carry both.</summary>
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

            foreach (var alias in AliasesOf(row.DescriptionUrl))
            {
                aliases.TryAdd(alias, key);
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

        foreach (var alias in AliasesOf(row.FileUrl).Concat(AliasesOf(row.DescriptionUrl)))
        {
            if (aliases.TryGetValue(alias, out var mapped))
            {
                return mapped;
            }
        }

        return key;
    }

    // thepiratebay.org/description.php?id=… and Knaben's knaben.xyz/thepiratebay/description.php?id=…
    [GeneratedRegex(@"thepiratebay(?:\.[a-z]+)?/description\.php\?id=(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TpbDescriptionRegex();

    // www.limetorrents.lol/Name-torrent-1580228.html (domain changes: .lol, .fun...)
    [GeneratedRegex(@"limetorrents\.[a-z]+/.*-torrent-(?<id>\d+)\.html", RegexOptions.IgnoreCase)]
    private static partial Regex LimeDetailRegex();
}
