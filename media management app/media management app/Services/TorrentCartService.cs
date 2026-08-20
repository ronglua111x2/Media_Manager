using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public interface ITorrentCartService
{
    event EventHandler? CartChanged;

    TorrentCartOrder? GetOrder(long orderId);

    IReadOnlyList<TorrentCartOrder> GetOrders(MediaKind mediaKind, long mediaId);

    IReadOnlyList<TorrentCartOrderCandidate> GetCandidates(long orderId);

    int GetOrderCount(MediaKind mediaKind, long mediaId);

    TorrentCartOrder AddEpisodeOrder(long showId, long episodeId, int seasonNumber, int episodeNumber, string title);

    TorrentCartOrder AddSeasonPackOrder(long showId, int seasonNumber);

    TorrentCartOrder AddMovieOrder(long movieId, string title);

    bool TryGetActiveEpisodeOrder(long episodeId, out TorrentCartOrder? order);

    bool TryGetAutoTrackHuntBlockingEpisodeOrder(long episodeId, out TorrentCartOrder? order);

    TorrentCartOrder PrepareAutoTrackEpisodeOrder(long showId, long episodeId, int seasonNumber, int episodeNumber, string title);

    bool HasActiveManualEpisodeOrder(long episodeId);

    bool TryGetActiveMovieOrder(long movieId, out TorrentCartOrder? order);

    bool TryGetActiveSeasonPackOrder(long showId, int seasonNumber, out TorrentCartOrder? order);

    int ClearCart(MediaKind mediaKind, long mediaId);

    int ClearAllCarts();

    int ClearCandidates(MediaKind mediaKind, long mediaId, IEnumerable<long>? orderIds = null);

    int RemoveOrder(long orderId);

    void SaveOrder(TorrentCartOrder order);

    void ReplaceCandidates(long orderId, IReadOnlyList<TorrentCartOrderCandidate> candidates);

    bool RemoveCandidate(long orderId, long candidateId);

    void SelectCandidate(long orderId, long candidateId);

    void AcceptSelectedCandidate(long orderId);

    int AcceptSelectedCandidates(MediaKind mediaKind, long mediaId, IReadOnlyList<long>? orderIds = null);

    void UpdateOrderStatus(long orderId, TorrentOrderStatus status, string statusDetail = "");

    /// <summary>
    /// Marks stuck/blocking auto-track cart orders as Failed so pending episodes can be hunted again.
    /// Does not touch orders that are already downloading or completed.
    /// </summary>
    int ReleaseAutoTrackHuntBlocks(long showId);
}

public sealed class TorrentCartService : ITorrentCartService
{
    private readonly IDatabaseService _databaseService;
    private readonly IAppLogger _logger;

    public TorrentCartService(IDatabaseService databaseService, IAppLogger logger)
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    public event EventHandler? CartChanged;

    public TorrentCartOrder? GetOrder(long orderId)
    {
        return _databaseService.GetTorrentCartOrder(orderId);
    }

    public IReadOnlyList<TorrentCartOrder> GetOrders(MediaKind mediaKind, long mediaId)
    {
        return _databaseService.GetTorrentCartOrders(mediaKind, mediaId);
    }

    public IReadOnlyList<TorrentCartOrderCandidate> GetCandidates(long orderId)
    {
        return _databaseService.GetTorrentCartOrderCandidates(orderId);
    }

    public int GetOrderCount(MediaKind mediaKind, long mediaId)
    {
        return _databaseService.GetTorrentCartOrders(mediaKind, mediaId).Count;
    }

    public TorrentCartOrder AddEpisodeOrder(long showId, long episodeId, int seasonNumber, int episodeNumber, string title)
    {
        if (TryGetActiveEpisodeOrder(episodeId, out _))
        {
            throw new InvalidOperationException($"Episode S{seasonNumber:00}E{episodeNumber:00} is already in the cart.");
        }

        var order = new TorrentCartOrder
        {
            TargetKind = MediaKind.TvEpisode,
            MediaId = showId,
            EpisodeId = episodeId,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            Title = $"S{seasonNumber:00}E{episodeNumber:00} - {title}",
            Summary = "Episode order",
            Status = TorrentOrderStatus.Draft,
            Source = TorrentOrderSource.Manual
        };
        _databaseService.UpsertTorrentCartOrder(order);
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
            TargetKind = MediaKind.TvEpisode,
            MediaId = showId,
            SeasonNumber = seasonNumber,
            Title = $"Season {seasonNumber:00} Pack",
            Summary = "Season pack order",
            Status = TorrentOrderStatus.Draft
        };
        _databaseService.UpsertTorrentCartOrder(order);
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
            TargetKind = MediaKind.Movie,
            MediaId = movieId,
            Title = title,
            Summary = "Movie order",
            Status = TorrentOrderStatus.Draft
        };
        _databaseService.UpsertTorrentCartOrder(order);
        NotifyChanged();
        return order;
    }

    public bool TryGetActiveEpisodeOrder(long episodeId, out TorrentCartOrder? order)
    {
        order = _databaseService.GetTorrentCartOrders().FirstOrDefault(item =>
            item.EpisodeId == episodeId &&
            item.Status is not TorrentOrderStatus.Completed and not TorrentOrderStatus.Canceled and not TorrentOrderStatus.Failed);
        return order is not null;
    }

    public bool TryGetAutoTrackHuntBlockingEpisodeOrder(long episodeId, out TorrentCartOrder? order)
    {
        order = _databaseService.GetTorrentCartOrders().FirstOrDefault(item =>
            item.EpisodeId == episodeId &&
            item.Status is TorrentOrderStatus.Searching
                or TorrentOrderStatus.CandidatesFound
                or TorrentOrderStatus.Approved
                or TorrentOrderStatus.AddedToClient
                or TorrentOrderStatus.Downloading);
        return order is not null;
    }

    public TorrentCartOrder PrepareAutoTrackEpisodeOrder(long showId, long episodeId, int seasonNumber, int episodeNumber, string title)
    {
        if (TryGetAutoTrackHuntBlockingEpisodeOrder(episodeId, out _))
        {
            throw new InvalidOperationException($"Episode S{seasonNumber:00}E{episodeNumber:00} hunt is already in progress.");
        }

        if (HasActiveManualEpisodeOrder(episodeId))
        {
            throw new InvalidOperationException($"Episode S{seasonNumber:00}E{episodeNumber:00} has an active manual cart order.");
        }

        var existing = _databaseService.GetTorrentCartOrders().FirstOrDefault(item => item.EpisodeId == episodeId);
        if (existing is not null &&
            existing.Source == TorrentOrderSource.AutoTrack &&
            existing.Status is TorrentOrderStatus.Draft
                or TorrentOrderStatus.NoCandidates
                or TorrentOrderStatus.Failed)
        {
            _databaseService.DeleteTorrentCartOrderCandidates(existing.Id);
            ClearSelectedCandidate(existing);
            ClearTorrentState(existing);
            existing.FailedCandidateUrls = string.Empty;
            existing.LastFailureReason = string.Empty;
            existing.Status = TorrentOrderStatus.Draft;
            existing.StatusDetail = string.Empty;
            existing.Source = TorrentOrderSource.AutoTrack;
            existing.Summary = "Auto-track episode order";
            _databaseService.UpsertTorrentCartOrder(existing);
            NotifyChanged();
            return existing;
        }

        if (existing is not null)
        {
            throw new InvalidOperationException($"Episode S{seasonNumber:00}E{episodeNumber:00} already has a cart order.");
        }

        var order = new TorrentCartOrder
        {
            TargetKind = MediaKind.TvEpisode,
            MediaId = showId,
            EpisodeId = episodeId,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            Title = $"S{seasonNumber:00}E{episodeNumber:00} - {title}",
            Summary = "Auto-track episode order",
            Status = TorrentOrderStatus.Draft,
            Source = TorrentOrderSource.AutoTrack
        };
        _databaseService.UpsertTorrentCartOrder(order);
        NotifyChanged();
        return order;
    }

    public bool HasActiveManualEpisodeOrder(long episodeId)
    {
        return _databaseService.GetTorrentCartOrders().Any(item =>
            item.EpisodeId == episodeId &&
            item.Source == TorrentOrderSource.Manual &&
            item.Status is not TorrentOrderStatus.Completed
                and not TorrentOrderStatus.Canceled
                and not TorrentOrderStatus.Failed);
    }

    public bool TryGetActiveMovieOrder(long movieId, out TorrentCartOrder? order)
    {
        order = _databaseService.GetTorrentCartOrders(MediaKind.Movie, movieId).FirstOrDefault(item =>
            item.TargetKind == MediaKind.Movie &&
            item.MediaId == movieId &&
            item.Status is not TorrentOrderStatus.Completed and not TorrentOrderStatus.Canceled and not TorrentOrderStatus.Failed);
        return order is not null;
    }

    public bool TryGetActiveSeasonPackOrder(long showId, int seasonNumber, out TorrentCartOrder? order)
    {
        order = _databaseService.GetTorrentCartOrders(MediaKind.TvEpisode, showId).FirstOrDefault(item =>
            item.MediaId == showId &&
            item.SeasonNumber == seasonNumber &&
            item.EpisodeId is null &&
            item.Status is not TorrentOrderStatus.Completed and not TorrentOrderStatus.Canceled and not TorrentOrderStatus.Failed);
        return order is not null;
    }

    public int ClearCart(MediaKind mediaKind, long mediaId)
    {
        var removed = _databaseService.DeleteTorrentCartOrders(mediaKind, mediaId);
        if (removed > 0)
        {
            NotifyChanged();
        }

        return removed;
    }

    public int ClearAllCarts()
    {
        var removed = _databaseService.DeleteTorrentCartOrders();
        if (removed > 0)
        {
            NotifyChanged();
        }

        return removed;
    }

    public int ClearCandidates(MediaKind mediaKind, long mediaId, IEnumerable<long>? orderIds = null)
    {
        var orders = _databaseService.GetTorrentCartOrders(mediaKind, mediaId);
        if (orderIds is not null)
        {
            var idSet = orderIds.ToHashSet();
            orders = orders.Where(order => idSet.Contains(order.Id)).ToList();
        }

        var clearedCount = 0;
        foreach (var order in orders)
        {
            var removed = _databaseService.DeleteTorrentCartOrderCandidates(order.Id);
            if (removed == 0 &&
                order.Status is not TorrentOrderStatus.CandidatesFound
                    and not TorrentOrderStatus.NoCandidates
                    and not TorrentOrderStatus.Approved
                    and not TorrentOrderStatus.Failed)
            {
                continue;
            }

            ClearSelectedCandidate(order);
            ClearTorrentState(order);
            if (order.Status is TorrentOrderStatus.CandidatesFound
                or TorrentOrderStatus.NoCandidates
                or TorrentOrderStatus.Approved
                or TorrentOrderStatus.Failed)
            {
                order.Status = TorrentOrderStatus.Draft;
                order.StatusDetail = string.Empty;
            }

            _databaseService.UpsertTorrentCartOrder(order);
            clearedCount++;
        }

        if (clearedCount > 0)
        {
            NotifyChanged();
        }

        return clearedCount;
    }

    public int RemoveOrder(long orderId)
    {
        var removed = _databaseService.DeleteTorrentCartOrder(orderId);
        if (removed > 0)
        {
            NotifyChanged();
        }

        return removed;
    }

    public void SaveOrder(TorrentCartOrder order)
    {
        _databaseService.UpsertTorrentCartOrder(order);
        NotifyChanged();
    }

    public void ReplaceCandidates(long orderId, IReadOnlyList<TorrentCartOrderCandidate> candidates)
    {
        var order = _databaseService.GetTorrentCartOrder(orderId);
        if (order is null)
        {
            return;
        }

        var rankedCandidates = candidates
            .OrderByDescending(candidate => candidate.TotalScore)
            .ThenByDescending(candidate => candidate.Seeders)
            .Select((candidate, index) =>
            {
                candidate.OrderId = orderId;
                candidate.Rank = index + 1;
                candidate.IsSelected = index == 0;
                candidate.IsAccepted = false;
                return candidate;
            })
            .ToList();
        _databaseService.ReplaceTorrentCartOrderCandidates(orderId, rankedCandidates);

        var selected = _databaseService.GetTorrentCartOrderCandidates(orderId).FirstOrDefault(candidate => candidate.IsSelected);
        if (selected is not null)
        {
            ApplyCandidate(order, selected);
            order.FailedCandidateUrls = string.Empty;
            order.LastFailureReason = string.Empty;
            order.Status = TorrentOrderStatus.CandidatesFound;
            order.StatusDetail = $"Candidate found: {selected.Name}";
            _databaseService.UpsertTorrentCartOrder(order);
        }

        NotifyChanged();
    }

    public bool RemoveCandidate(long orderId, long candidateId)
    {
        var order = _databaseService.GetTorrentCartOrder(orderId);
        if (order is null)
        {
            return false;
        }

        var candidates = _databaseService.GetTorrentCartOrderCandidates(orderId);
        var removed = candidates.FirstOrDefault(item => item.Id == candidateId);
        if (removed is null)
        {
            return false;
        }

        if (_databaseService.DeleteTorrentCartOrderCandidate(candidateId) <= 0)
        {
            return false;
        }

        if (removed.IsSelected)
        {
            var next = _databaseService.GetTorrentCartOrderCandidates(orderId)
                .OrderBy(item => item.Rank)
                .FirstOrDefault();
            if (next is not null)
            {
                _databaseService.UpdateTorrentCartOrderCandidateSelection(orderId, next.Id);
                ApplyCandidate(order, next);
            }
            else
            {
                ClearSelectedCandidate(order);
            }

            _databaseService.UpsertTorrentCartOrder(order);
        }

        NotifyChanged();
        return true;
    }

    public void SelectCandidate(long orderId, long candidateId)
    {
        var order = _databaseService.GetTorrentCartOrder(orderId);
        var candidate = _databaseService.GetTorrentCartOrderCandidates(orderId)
            .FirstOrDefault(item => item.Id == candidateId);
        if (order is null || candidate is null)
        {
            return;
        }

        _databaseService.UpdateTorrentCartOrderCandidateSelection(orderId, candidateId);
        _databaseService.UpdateTorrentCartOrderCandidateAccepted(orderId, candidateId, isAccepted: false);
        ApplyCandidate(order, candidate);
        if (order.Status is TorrentOrderStatus.Approved or TorrentOrderStatus.Failed)
        {
            if (order.Status == TorrentOrderStatus.Failed)
            {
                ClearTorrentState(order);
            }

            order.Status = TorrentOrderStatus.CandidatesFound;
        }

        order.StatusDetail = $"Selected candidate: {candidate.Name}";
        _databaseService.UpsertTorrentCartOrder(order);
        NotifyChanged();
    }

    public void AcceptSelectedCandidate(long orderId)
    {
        var order = _databaseService.GetTorrentCartOrder(orderId);
        var selected = _databaseService.GetTorrentCartOrderCandidates(orderId)
            .FirstOrDefault(candidate => candidate.IsSelected);
        if (order is null || selected is null)
        {
            return;
        }

        _databaseService.UpdateTorrentCartOrderCandidateAccepted(orderId, selected.Id, isAccepted: true);
        ApplyCandidate(order, selected);
        order.Status = TorrentOrderStatus.Approved;
        order.StatusDetail = $"Accepted candidate: {selected.Name}";
        _databaseService.UpsertTorrentCartOrder(order);
        NotifyChanged();
    }

    public int AcceptSelectedCandidates(MediaKind mediaKind, long mediaId, IReadOnlyList<long>? orderIds = null)
    {
        var acceptedCount = 0;
        var orders = _databaseService.GetTorrentCartOrders(mediaKind, mediaId);
        if (orderIds is not null)
        {
            var idSet = orderIds.ToHashSet();
            orders = orders.Where(order => idSet.Contains(order.Id)).ToList();
        }

        foreach (var order in orders)
        {
            var selected = _databaseService.GetTorrentCartOrderCandidates(order.Id)
                .FirstOrDefault(candidate => candidate.IsSelected);
            if (selected is null)
            {
                continue;
            }

            _databaseService.UpdateTorrentCartOrderCandidateAccepted(order.Id, selected.Id, isAccepted: true);
            ApplyCandidate(order, selected);
            order.Status = TorrentOrderStatus.Approved;
            order.StatusDetail = $"Accepted candidate: {selected.Name}";
            _databaseService.UpsertTorrentCartOrder(order);
            acceptedCount++;
        }

        if (acceptedCount > 0)
        {
            NotifyChanged();
        }

        return acceptedCount;
    }

    public void UpdateOrderStatus(long orderId, TorrentOrderStatus status, string statusDetail = "")
    {
        var order = _databaseService.GetTorrentCartOrder(orderId);
        if (order is null)
        {
            return;
        }

        order.Status = status;
        order.StatusDetail = statusDetail;
        _databaseService.UpsertTorrentCartOrder(order);
        NotifyChanged();
    }

    public int ReleaseAutoTrackHuntBlocks(long showId)
    {
        var released = 0;
        foreach (var order in _databaseService.GetTorrentCartOrders(MediaKind.TvEpisode, showId))
        {
            if (order.Source != TorrentOrderSource.AutoTrack)
            {
                continue;
            }

            // Leave live downloads alone; only clear hunt-queue blockers / stuck searches.
            if (order.Status is not (TorrentOrderStatus.Draft
                or TorrentOrderStatus.Searching
                or TorrentOrderStatus.CandidatesFound
                or TorrentOrderStatus.NoCandidates
                or TorrentOrderStatus.Approved
                or TorrentOrderStatus.Failed))
            {
                continue;
            }

            _databaseService.DeleteTorrentCartOrderCandidates(order.Id);
            ClearSelectedCandidate(order);
            ClearTorrentState(order);
            order.FailedCandidateUrls = string.Empty;
            order.LastFailureReason = string.Empty;
            order.Status = TorrentOrderStatus.Failed;
            order.StatusDetail = "Cleared by Reset week so hunt can run again.";
            _databaseService.UpsertTorrentCartOrder(order);
            released++;
        }

        if (released > 0)
        {
            _logger.Info(
                $"Reset week released {released} blocking auto-track cart order(s) for show {showId}.",
                LogTarget.File | LogTarget.Console);
            NotifyChanged();
        }

        return released;
    }

    private void NotifyChanged()
    {
        CartChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ClearSelectedCandidate(TorrentCartOrder order)
    {
        order.SelectedCandidateName = string.Empty;
        order.SelectedCandidateUrl = string.Empty;
        order.SelectedCandidatePlugin = string.Empty;
        order.SelectedCandidateFileSize = 0;
        order.SelectedCandidateSeeders = 0;
        order.SelectedCandidateLeechers = 0;
        order.SelectedCandidateQuality = string.Empty;
        order.SelectedCandidateAudioCodec = string.Empty;
        order.SelectedCandidateCoveredSeasons = string.Empty;
        order.SelectedCandidateContentProfile = string.Empty;
        order.SelectedCandidateTotalScore = 0;
    }

    private static void ClearTorrentState(TorrentCartOrder order)
    {
        order.TorrentHash = string.Empty;
        order.TorrentName = string.Empty;
        order.TorrentState = string.Empty;
        order.TorrentProgress = 0;
    }

    private static void ApplyCandidate(TorrentCartOrder order, TorrentCartOrderCandidate candidate)
    {
        order.SelectedCandidateName = candidate.Name;
        order.SelectedCandidateUrl = candidate.Url;
        order.SelectedCandidatePlugin = candidate.PluginName;
        order.SelectedCandidateFileSize = candidate.FileSize;
        order.SelectedCandidateSeeders = candidate.Seeders;
        order.SelectedCandidateLeechers = candidate.Leechers;
        order.SelectedCandidateQuality = candidate.Quality;
        order.SelectedCandidateAudioCodec = candidate.AudioCodec;
        order.SelectedCandidateCoveredSeasons = candidate.CoveredSeasons;
        order.SelectedCandidateContentProfile = candidate.ContentProfileJson;
        order.SelectedCandidateTotalScore = candidate.TotalScore;
    }
}
