using System.Windows.Threading;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

/// <summary>
/// Find "Add + search" entry point (<see cref="ICartSearchLauncher"/>) and the per-order
/// "Refresh in Jellyfin" icon.
/// </summary>
public sealed partial class TorrentWorkspaceViewModel : ICartSearchLauncher
{
    private sealed record QueuedCartSearch(MediaKind Kind, long MediaId, string Title, IReadOnlyCollection<long> OrderIds);

    private readonly Queue<QueuedCartSearch> _queuedSearches = new();
    private bool _queuedSearchRunning;

    public event EventHandler<CartSearchFinishedEventArgs>? SearchFinished;

    public void QueueSearch(MediaKind kind, long mediaId, string title, IReadOnlyCollection<long> orderIds)
    {
        if (orderIds.Count == 0)
        {
            return;
        }

        _queuedSearches.Enqueue(new QueuedCartSearch(kind, mediaId, title, orderIds.ToList()));
        _logger.Info(
            $"Cart quick search queued: Kind={kind}, MediaId={mediaId}, Orders={orderIds.Count}, Waiting={_queuedSearches.Count}, CartBusy={IsRunningCart}.",
            LogTarget.All);
        TryStartQueuedSearch();
    }

    public void OpenCart(MediaKind kind, long mediaId)
    {
        _workspaceNavigator.NavigateTo(AppWorkspaceKind.Torrent);
        var card = FindCard(kind, mediaId);
        if (card is null && HasAppliedMediaSearch)
        {
            MediaSearchText = string.Empty;
            MediaSearchQuery = string.Empty;
            card = FindCard(kind, mediaId);
        }

        if (card is not null)
        {
            SelectedMediaCard = card;
        }
    }

    private LibraryMediaCardViewModel? FindCard(MediaKind kind, long mediaId) =>
        MediaCards.FirstOrDefault(card => card.Id == mediaId && card.MediaKind == kind);

    /// <summary>Starts the next queued search when no cart operation (run, add, retry, reconcile) is running.</summary>
    private void TryStartQueuedSearch()
    {
        if (_queuedSearchRunning || IsRunningCart || _queuedSearches.Count == 0)
        {
            return;
        }

        _ = RunQueuedSearchAsync(_queuedSearches.Dequeue());
    }

    private async Task RunQueuedSearchAsync(QueuedCartSearch request)
    {
        _queuedSearchRunning = true;
        var status = string.Empty;
        try
        {
            var orders = GetRunnableOrders(request.Kind, request.MediaId, request.OrderIds);
            if (orders.Count == 0)
            {
                status = "No runnable orders left to search.";
                return;
            }

            StatusMessage = $"Searching {orders.Count} order(s) for {request.Title}...";
            status = await RunOrdersSearchAsync(
                request.Kind,
                request.MediaId,
                orders,
                StoredSearchContext(request.Kind, request.MediaId));
            StatusMessage = $"{request.Title}: {status}";
        }
        catch (Exception ex)
        {
            status = $"Search failed: {ex.Message}";
            _logger.Error($"Cart quick search failed for MediaId={request.MediaId}.", ex, LogTarget.All);
        }
        finally
        {
            _queuedSearchRunning = false;
            SearchFinished?.Invoke(this, new CartSearchFinishedEventArgs(request.Kind, request.MediaId, request.Title, status));
            TryStartQueuedSearch();
        }
    }

    partial void OnIsRunningCartChanged(bool oldValue, bool newValue)
    {
        if (newValue || _queuedSearches.Count == 0)
        {
            return;
        }

        // Let the finishing operation unwind first, then run what Find queued meanwhile.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            TryStartQueuedSearch();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, TryStartQueuedSearch);
    }

    private async Task RefreshOrderInJellyfinAsync(TorrentOrderViewModel order)
    {
        try
        {
            var paths = GetOrderLinkPaths(order);
            StatusMessage = $"Sending Jellyfin refresh for {order.Title} ({paths.Count} path(s))...";
            var result = await _jellyfinLibraryRefreshService.RefreshNowAsync(paths);
            StatusMessage = $"{order.Title}: {result.ToStatusText()}";
            await Task.Delay(LibraryViewModel.JellyfinRefreshCooldown);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Jellyfin refresh failed for {order.Title}: {ex.Message}";
        }
    }

    private IReadOnlyList<string> GetOrderLinkPaths(TorrentOrderViewModel order)
    {
        var sourceItems = _databaseService.GetSourceItems();
        if (order.TargetKind == MediaKind.Movie)
        {
            var movie = _databaseService.GetTrackedMovie(order.MediaId);
            return movie is null ? [] : JellyfinRefreshTargets.ForMovie(movie.TmdbId, sourceItems);
        }

        var show = _databaseService.GetTrackedShow(order.MediaId);
        if (show is null)
        {
            return [];
        }

        if (order.EpisodeId is not null)
        {
            var episode = _trackedShowService.GetEpisodes(show.Id).FirstOrDefault(item => item.Id == order.EpisodeId);
            return episode is null
                ? []
                : JellyfinRefreshTargets.ForShow(show.TmdbId, sourceItems, episode.SeasonNumber, episode.EpisodeNumber);
        }

        return JellyfinRefreshTargets.ForShow(show.TmdbId, sourceItems, order.SeasonNumber);
    }
}
