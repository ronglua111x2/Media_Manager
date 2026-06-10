using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface ITorrentCartService
{
    event EventHandler? CartChanged;

    IReadOnlyList<TorrentCartOrder> GetOrders(MediaKind mediaKind, long mediaId);

    int GetOrderCount(MediaKind mediaKind, long mediaId);

    TorrentCartOrder AddEpisodeOrder(long showId, long episodeId, int seasonNumber, int episodeNumber, string title);

    TorrentCartOrder AddSeasonPackOrder(long showId, int seasonNumber);

    TorrentCartOrder AddMovieOrder(long movieId, string title);

    bool TryGetActiveEpisodeOrder(long episodeId, out TorrentCartOrder? order);

    bool TryGetActiveMovieOrder(long movieId, out TorrentCartOrder? order);

    bool TryGetActiveSeasonPackOrder(long showId, int seasonNumber, out TorrentCartOrder? order);

    int ClearCart(MediaKind mediaKind, long mediaId);

    int ClearAllCarts();

    int RemoveOrder(long orderId);

    void UpdateOrderStatus(long orderId, TorrentOrderStatus status, string statusDetail = "");
}

public sealed class TorrentCartService : ITorrentCartService
{
    private readonly List<TorrentCartOrder> _orders = [];
    private long _nextOrderId = 1;

    public event EventHandler? CartChanged;

    public IReadOnlyList<TorrentCartOrder> GetOrders(MediaKind mediaKind, long mediaId)
    {
        return _orders
            .Where(order => order.TargetKind == mediaKind && order.MediaId == mediaId)
            .OrderBy(order => order.Id)
            .ToList();
    }

    public int GetOrderCount(MediaKind mediaKind, long mediaId)
    {
        return _orders.Count(order => order.TargetKind == mediaKind && order.MediaId == mediaId);
    }

    public TorrentCartOrder AddEpisodeOrder(long showId, long episodeId, int seasonNumber, int episodeNumber, string title)
    {
        if (TryGetActiveEpisodeOrder(episodeId, out _))
        {
            throw new InvalidOperationException($"Episode S{seasonNumber:00}E{episodeNumber:00} is already in the cart.");
        }

        var order = new TorrentCartOrder
        {
            Id = _nextOrderId++,
            TargetKind = MediaKind.TvEpisode,
            MediaId = showId,
            EpisodeId = episodeId,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            Title = $"S{seasonNumber:00}E{episodeNumber:00} - {title}",
            Summary = "Episode order",
            Status = TorrentOrderStatus.Draft
        };
        _orders.Add(order);
        NotifyChanged();
        return order;
    }

    public TorrentCartOrder AddSeasonPackOrder(long showId, int seasonNumber)
    {
        if (TryGetActiveSeasonPackOrder(showId, seasonNumber, out _))
        {
            throw new InvalidOperationException($"Season {seasonNumber:00} pack is already in the cart.");
        }

        var order = new TorrentCartOrder
        {
            Id = _nextOrderId++,
            TargetKind = MediaKind.TvEpisode,
            MediaId = showId,
            SeasonNumber = seasonNumber,
            Title = $"Season {seasonNumber:00} Pack",
            Summary = "Season pack order",
            Status = TorrentOrderStatus.Draft
        };
        _orders.Add(order);
        NotifyChanged();
        return order;
    }

    public TorrentCartOrder AddMovieOrder(long movieId, string title)
    {
        if (TryGetActiveMovieOrder(movieId, out _))
        {
            throw new InvalidOperationException($"Movie '{title}' is already in the cart.");
        }

        var order = new TorrentCartOrder
        {
            Id = _nextOrderId++,
            TargetKind = MediaKind.Movie,
            MediaId = movieId,
            Title = title,
            Summary = "Movie order",
            Status = TorrentOrderStatus.Draft
        };
        _orders.Add(order);
        NotifyChanged();
        return order;
    }

    public bool TryGetActiveEpisodeOrder(long episodeId, out TorrentCartOrder? order)
    {
        order = _orders.FirstOrDefault(item =>
            item.EpisodeId == episodeId &&
            item.Status is not TorrentOrderStatus.Completed and not TorrentOrderStatus.Canceled and not TorrentOrderStatus.Failed);
        return order is not null;
    }

    public bool TryGetActiveMovieOrder(long movieId, out TorrentCartOrder? order)
    {
        order = _orders.FirstOrDefault(item =>
            item.TargetKind == MediaKind.Movie &&
            item.MediaId == movieId &&
            item.Status is not TorrentOrderStatus.Completed and not TorrentOrderStatus.Canceled and not TorrentOrderStatus.Failed);
        return order is not null;
    }

    public bool TryGetActiveSeasonPackOrder(long showId, int seasonNumber, out TorrentCartOrder? order)
    {
        order = _orders.FirstOrDefault(item =>
            item.MediaId == showId &&
            item.SeasonNumber == seasonNumber &&
            item.EpisodeId is null &&
            item.Status is not TorrentOrderStatus.Completed and not TorrentOrderStatus.Canceled and not TorrentOrderStatus.Failed);
        return order is not null;
    }

    public int ClearCart(MediaKind mediaKind, long mediaId)
    {
        var removed = _orders.RemoveAll(order => order.TargetKind == mediaKind && order.MediaId == mediaId);
        if (removed > 0)
        {
            NotifyChanged();
        }

        return removed;
    }

    public int ClearAllCarts()
    {
        var removed = _orders.Count;
        _orders.Clear();
        if (removed > 0)
        {
            NotifyChanged();
        }

        return removed;
    }

    public int RemoveOrder(long orderId)
    {
        var removed = _orders.RemoveAll(order => order.Id == orderId);
        if (removed > 0)
        {
            NotifyChanged();
        }

        return removed;
    }

    public void UpdateOrderStatus(long orderId, TorrentOrderStatus status, string statusDetail = "")
    {
        var order = _orders.FirstOrDefault(item => item.Id == orderId);
        if (order is null)
        {
            return;
        }

        order.Status = status;
        order.StatusDetail = statusDetail;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        CartChanged?.Invoke(this, EventArgs.Empty);
    }
}
