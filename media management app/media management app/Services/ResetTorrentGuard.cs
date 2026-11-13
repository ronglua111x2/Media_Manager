using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

/// <summary>
/// The torrents an order's target was reset from ("Reset for redownload", cross-review 2 B): the episode row, the
/// season rows a pack covers, or the movie row. The hunt and the cart never attach such a torrent to that target
/// again, neither from search results nor as an "already in qBittorrent" copy. Other targets may still use it.
/// </summary>
public static class ResetTorrentGuard
{
    /// <summary>A predicate "was the order's target reset from this infohash" (case-insensitive; null/empty = no).</summary>
    public static Func<string?, bool> For(TorrentCartOrder order, IDatabaseService databaseService)
    {
        var lists = order.TargetKind switch
        {
            MediaKind.TvEpisode when order.EpisodeId is { } episodeId =>
                databaseService.GetTrackedEpisodes(order.MediaId)
                    .Where(episode => episode.Id == episodeId)
                    .Select(episode => episode.ResetTorrentHashes)
                    .ToList(),
            MediaKind.TvSeasonPack => PackSeasonsResetLists(order, databaseService),
            MediaKind.Movie => [databaseService.GetTrackedMovie(order.MediaId)?.ResetTorrentHashes ?? string.Empty],
            _ => []
        };

        return hash => lists.Any(list => ResetTorrentHashList.Contains(list, hash));
    }

    private static List<string> PackSeasonsResetLists(TorrentCartOrder order, IDatabaseService databaseService)
    {
        var seasons = (order.SelectedCandidateCoveredSeasons ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => int.TryParse(text, out var value) ? value : (int?)null)
            .OfType<int>()
            .ToHashSet();
        if (order.SeasonNumber is { } owner)
        {
            seasons.Add(owner);
        }

        return databaseService.GetTrackedSeasons(order.MediaId)
            .Where(season => seasons.Contains(season.SeasonNumber))
            .Select(season => season.ResetTorrentHashes)
            .ToList();
    }
}

/// <summary>The selected candidate is the torrent its target was reset from: try another candidate.</summary>
public sealed class ResetTorrentReusedException(string message) : InvalidOperationException(message);
