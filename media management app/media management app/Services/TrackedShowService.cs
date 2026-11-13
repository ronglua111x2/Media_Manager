using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class TrackedShowService : ITrackedShowService
{
    private readonly IDatabaseService _databaseService;
    private readonly ITmdbShowCatalogService _catalogService;
    private readonly IPosterImageService _posterImageService;
    private readonly IAutoTorrentLinkService _autoTorrentLinkService;
    private readonly IAppLogger _logger;

    public TrackedShowService(
        IDatabaseService databaseService,
        ITmdbShowCatalogService catalogService,
        IPosterImageService posterImageService,
        IAutoTorrentLinkService autoTorrentLinkService,
        IAppLogger logger)
    {
        _databaseService = databaseService;
        _catalogService = catalogService;
        _posterImageService = posterImageService;
        _autoTorrentLinkService = autoTorrentLinkService;
        _logger = logger;
    }

    public Task<IReadOnlyList<TmdbShowSearchResult>> SearchShowsAsync(string query, CancellationToken cancellationToken = default)
    {
        return _catalogService.SearchTvShowsAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<TmdbEpisodeGroupSummary>> GetEpisodeGroupsAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        return _catalogService.GetTvEpisodeGroupsAsync(tmdbId, cancellationToken);
    }

    public Task<TmdbShowDetails> GetShowSummaryAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        return _catalogService.GetTvShowSummaryAsync(tmdbId, cancellationToken);
    }

    public async Task<TrackedShow> AddShowAsync(
        TmdbShowSearchResult result,
        string? episodeGroupId = null,
        string? episodeGroupName = null,
        CancellationToken cancellationToken = default)
    {
        var details = await ResolveShowDetailsAsync(result.TmdbId, episodeGroupId, cancellationToken);
        var showId = ImportShow(details, "1080p", episodeGroupId, episodeGroupName);
        await CachePosterAsync(details.TmdbId, details.PosterPath, cancellationToken);
        RefreshAvailability(showId);
        var show = _databaseService.GetTrackedShow(showId) ?? throw new InvalidOperationException("Tracked show was not saved.");
        _logger.Info(
            $"Tracked show added: {show.DisplayTitle}, Episodes={show.TotalEpisodes}, Organization={show.EpisodeOrganizationLabel}",
            LogTarget.All);
        return show;
    }

    public async Task<TrackedShow> ImportShowByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        var details = await _catalogService.GetTvShowDetailsAsync(tmdbId, cancellationToken);
        var showId = ImportShow(details, "1080p");
        await CachePosterAsync(details.TmdbId, details.PosterPath, cancellationToken);
        RefreshAvailability(showId);
        var show = _databaseService.GetTrackedShow(showId) ?? throw new InvalidOperationException("Tracked show was not imported.");
        _logger.Info($"Tracked show imported from existing media: {show.DisplayTitle}, Episodes={show.TotalEpisodes}", LogTarget.All);
        return show;
    }

    public async Task<TrackedShow> RefreshShowAsync(TrackedShow show, CancellationToken cancellationToken = default)
    {
        // Always re-read organization from DB so Sync/AutoTrack cannot use a stale in-memory route.
        var persisted = _databaseService.GetTrackedShow(show.Id) ?? show;
        var episodeGroupId = persisted.EpisodeGroupId;
        var episodeGroupName = persisted.EpisodeGroupName;

        var details = await ResolveShowDetailsAsync(persisted.TmdbId, episodeGroupId, cancellationToken);
        var showId = ImportShow(details, persisted.PreferredQuality, episodeGroupId, episodeGroupName);
        await CachePosterAsync(details.TmdbId, details.PosterPath, cancellationToken);
        RefreshAvailability(showId);
        var refreshed = _databaseService.GetTrackedShow(showId) ?? throw new InvalidOperationException("Tracked show was not refreshed.");
        _logger.Info(
            $"Tracked show refreshed: {refreshed.DisplayTitle}, Episodes={refreshed.TotalEpisodes}, Organization={refreshed.EpisodeOrganizationLabel}",
            LogTarget.All);
        return refreshed;
    }

    public async Task<TrackedShow> SwitchEpisodeOrganizationAsync(
        TrackedShow show,
        string? episodeGroupId,
        string? episodeGroupName,
        CancellationToken cancellationToken = default)
    {
        var normalizedGroupId = string.IsNullOrWhiteSpace(episodeGroupId) ? null : episodeGroupId.Trim();
        var normalizedGroupName = normalizedGroupId is null
            ? null
            : (string.IsNullOrWhiteSpace(episodeGroupName) ? "Episode Group" : episodeGroupName.Trim());

        var currentGroupId = string.IsNullOrWhiteSpace(show.EpisodeGroupId) ? null : show.EpisodeGroupId.Trim();
        var isSameRoute = string.Equals(currentGroupId, normalizedGroupId, StringComparison.Ordinal);

        // Fetch first: if TMDB fails, the show, its links and its records stay as they were (audit L3-01).
        var details = await ResolveShowDetailsAsync(show.TmdbId, normalizedGroupId, cancellationToken);

        if (isSameRoute && KeepsEveryEpisodeNumber(show.Id, details))
        {
            // Re-applying the same organization with unchanged numbering: nothing to renumber, so keep every
            // link and record and only refresh the episode list (audit L3-01).
            _databaseService.UpdateTrackedShowEpisodeOrganization(show.Id, normalizedGroupId, normalizedGroupName);
            var refreshedId = ImportShow(details, show.PreferredQuality, normalizedGroupId, normalizedGroupName);
            await CachePosterAsync(details.TmdbId, details.PosterPath, cancellationToken);
            RefreshAvailability(refreshedId);
            var refreshed = _databaseService.GetTrackedShow(refreshedId) ?? throw new InvalidOperationException("Tracked show was not refreshed.");
            _logger.Info(
                $"Tracked show organization re-applied with unchanged numbering, links kept: {refreshed.DisplayTitle} ({refreshed.EpisodeOrganizationLabel})",
                LogTarget.All);
            return refreshed;
        }

        // A library hardlink whose download is gone is the only copy of that file; removing it for the
        // renumbering would delete the media for good (audit L3-01).
        var onlyCopies = CountLinksThatAreTheOnlyCopy(show.TmdbId);
        if (onlyCopies > 0)
        {
            throw new InvalidOperationException(
                $"Episode organization was not changed: {onlyCopies} library file(s) of this show no longer have their download, " +
                "so the library file is the only copy and renumbering would delete it. " +
                "Keep the current organization, or re-add those torrents in qBittorrent (same files) so their downloads exist again, then retry.");
        }

        // Library links carry the old numbering: remove them before the rebuild, as the confirmation
        // dialog promises. If any link cannot be removed, change nothing (PARSE-26).
        var unlink = _autoTorrentLinkService.ClearShowLinksForRenumbering(show.Id);
        if (unlink.ErrorCount > 0)
        {
            throw new InvalidOperationException(
                $"Episode organization was not changed: {unlink.ErrorCount} library link(s) could not be removed: " +
                string.Join(" ", unlink.Messages.TakeLast(3)));
        }

        // Persist + hard-rebuild so mapping fixes and TMDB group edits are applied cleanly.
        _databaseService.UpdateTrackedShowEpisodeOrganization(show.Id, normalizedGroupId, normalizedGroupName);
        _databaseService.DeleteTrackedSeasonsAndEpisodes(show.Id);
        _databaseService.DeleteFetchJobsForMedia(show.Id, MediaKind.TvEpisode);

        var showId = ImportShow(details, show.PreferredQuality, normalizedGroupId, normalizedGroupName);
        await CachePosterAsync(details.TmdbId, details.PosterPath, cancellationToken);
        RefreshAvailability(showId);

        var rebuilt = _databaseService.GetTrackedShow(showId) ?? throw new InvalidOperationException("Tracked show was not rebuilt.");
        _logger.Info(
            isSameRoute
                ? $"Tracked show organization rebuilt in place: {rebuilt.DisplayTitle} ({rebuilt.EpisodeOrganizationLabel}), Episodes={rebuilt.TotalEpisodes}"
                : $"Tracked show organization switched: {rebuilt.DisplayTitle} -> {rebuilt.EpisodeOrganizationLabel}, Episodes={rebuilt.TotalEpisodes}",
            LogTarget.All);
        return rebuilt;
    }

    /// <summary>
    /// True when every episode the show has now is still in <paramref name="details"/> under the same number,
    /// with the same title and air date, so no existing link points at a different episode afterwards.
    /// </summary>
    private bool KeepsEveryEpisodeNumber(long showId, TmdbShowDetails details)
    {
        var current = _databaseService.GetTrackedEpisodes(showId);
        if (current.Count == 0)
        {
            return false;
        }

        var fetched = details.Seasons
            .SelectMany(season => season.Episodes)
            .GroupBy(episode => (episode.SeasonNumber, episode.EpisodeNumber))
            .ToDictionary(group => group.Key, group => group.First());

        return current.All(episode =>
            fetched.TryGetValue((episode.SeasonNumber, episode.EpisodeNumber), out var match) &&
            (!string.IsNullOrWhiteSpace(episode.Title) || episode.AirDate is not null) &&
            string.Equals(episode.Title?.Trim(), match.Title?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            episode.AirDate?.Date == match.AirDate?.Date);
    }

    private int CountLinksThatAreTheOnlyCopy(int tmdbId)
    {
        var providerId = tmdbId.ToString();
        return _databaseService.GetSourceItems().Count(item =>
            item.MediaKind == MediaKind.TvEpisode &&
            !item.IsExternalImport &&
            string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.LinkedPath) &&
            !string.Equals(item.LinkedPath, item.FilePath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(item.LinkedPath) &&
            !File.Exists(item.FilePath));
    }

    public IReadOnlyList<TrackedShow> GetShows()
    {
        return _databaseService.GetTrackedShows();
    }

    public IReadOnlyList<TrackedEpisode> GetEpisodes(long showId)
    {
        return _databaseService.GetTrackedEpisodes(showId);
    }

    public IReadOnlyList<TrackedSeason> GetSeasons(long showId)
    {
        return _databaseService.GetTrackedSeasons(showId);
    }

    public void RefreshAvailability()
    {
        var sourceItems = _databaseService.GetSourceItems();
        RefreshAvailability(sourceItems);
    }

    public void RefreshAvailability(IReadOnlyList<SourceItem> sourceItems)
    {
        foreach (var show in _databaseService.GetTrackedShows())
        {
            RefreshAvailabilityCore(show.Id, sourceItems);
        }
    }

    public void RefreshAvailability(long showId)
    {
        RefreshAvailabilityCore(showId, _databaseService.GetSourceItems());
    }

    public void RefreshAvailability(long showId, IReadOnlyList<SourceItem> sourceItems)
    {
        RefreshAvailabilityCore(showId, sourceItems);
    }

    public void UpdateSeasonDownloadFolder(long showId, int seasonNumber, string? downloadFolder)
    {
        _databaseService.UpdateTrackedSeasonDownloadFolder(showId, seasonNumber, downloadFolder);
        _logger.Info(
            $"Updated season download folder for show id={showId} S{seasonNumber:00}: '{downloadFolder ?? "<default>"}'",
            LogTarget.All);
    }

    public void UpdateSeasonPackMode(long showId, int seasonNumber, SeasonManagementMode mode)
    {
        _databaseService.UpdateTrackedSeasonPackMode(showId, seasonNumber, mode);
    }

    public void UpdateSeasonHidden(long showId, int seasonNumber, bool isHidden)
    {
        _databaseService.UpdateTrackedSeasonHidden(showId, seasonNumber, isHidden);
    }

    public void UpdateSeasonSelectedPack(long showId, int ownerSeasonNumber, SeasonPackCandidate candidate)
    {
        _databaseService.UpdateTrackedSeasonSelectedPack(showId, ownerSeasonNumber, candidate);
        _logger.Info($"Saved season pack for show id={showId} S{ownerSeasonNumber:00}: '{candidate.FileName}'", LogTarget.All);
    }

    public void ClearSeasonSelectedPacksForSeasons(long showId, IReadOnlyList<int> seasonNumbers)
    {
        _databaseService.ClearTrackedSeasonSelectedPacksForSeasons(showId, seasonNumbers);
        _logger.Info($"Cleared overlapping season packs for show id={showId}. Seasons={string.Join(",", seasonNumbers)}", LogTarget.All);
    }

    public void ClearSeasonSelectedPack(long showId, int ownerSeasonNumber)
    {
        _databaseService.ClearTrackedSeasonSelectedPack(showId, ownerSeasonNumber);
        _logger.Info($"Cleared season pack for show id={showId} S{ownerSeasonNumber:00}", LogTarget.All);
    }

    public void UpdateSeasonPackTorrent(long showId, int ownerSeasonNumber, AddedTorrentResult torrent)
    {
        _databaseService.UpdateTrackedSeasonPackTorrent(showId, ownerSeasonNumber, torrent);
    }

    public void MarkSeasonPackTorrentRemoved(long showId, int ownerSeasonNumber, string torrentHash)
    {
        _databaseService.MarkTrackedSeasonPackTorrentRemoved(showId, ownerSeasonNumber, torrentHash);
    }

    public void UpdateTorrentState(long episodeId, AddedTorrentResult torrent)
    {
        _databaseService.UpdateTrackedEpisodeTorrent(
            episodeId,
            torrent.Hash,
            torrent.Name,
            QbittorrentTorrentStateNormalizer.Normalize(torrent.State, torrent.IsComplete),
            torrent.Progress);
    }

    public void MarkTorrentRemoved(long episodeId, string torrentHash)
    {
        _databaseService.UpdateTrackedEpisodeTorrent(
            episodeId,
            string.Empty,
            string.Empty,
            string.Empty,
            0);
    }

    public void UpdateSelectedCandidate(long episodeId, EpisodeFetchCandidate candidate)
    {
        _databaseService.UpdateTrackedEpisodeSelectedCandidate(episodeId, candidate);
        _logger.Info($"Saved selected candidate for episode id={episodeId}: '{candidate.FileName}'", LogTarget.All);
    }

    public void UpdatePreferredQuality(long showId, string preferredQuality)
    {
        _databaseService.UpdateTrackedShowPreferredQuality(showId, preferredQuality);
    }

    public void UpdatePreferences(long showId, string preferredQuality, string preferredAudioCodec, int minimumSeeders)
    {
        _databaseService.UpdateTrackedShowPreferences(showId, preferredQuality, preferredAudioCodec, minimumSeeders);
        _logger.Info(
            $"Updated Auto Torrent preferences for show id={showId}: Quality='{preferredQuality}', Audio='{preferredAudioCodec}', MinimumSeeders={minimumSeeders}",
            LogTarget.All);
    }

    public void UpdateRecipe(long showId, string? recipeId)
    {
        _databaseService.UpdateTrackedShowRecipe(showId, recipeId);
        _logger.Info($"Updated recipe assignment for show id={showId}: {recipeId ?? "<default>"}", LogTarget.All);
    }

    public void UpdatePackRecipe(long showId, string? packRecipeId)
    {
        _databaseService.UpdateTrackedShowPackRecipe(showId, packRecipeId);
        _logger.Info($"Updated pack recipe assignment for show id={showId}: {packRecipeId ?? "<default>"}", LogTarget.All);
    }

    public void UpdateCartOverrides(long showId, MediaKind targetKind, CartRecipeOverrideSet overrides)
    {
        var json = CartRecipeOverrideSet.Serialize(overrides);
        _databaseService.UpdateTrackedShowCartOverridesJson(showId, targetKind, json);
        _logger.Info(
            $"Updated Cart recipe overrides for show id={showId}, target={targetKind}: {json ?? "<none>"}",
            LogTarget.All);
    }

    public void UpdateAutoTrackRecipeOverrides(long showId, CartRecipeOverrideSet overrides)
    {
        var json = CartRecipeOverrideSet.Serialize(overrides);
        _databaseService.UpdateTrackedShowAutoTrackRecipeOverridesJson(showId, json);
        _logger.Info(
            $"Updated Auto-Track episode recipe overrides for show id={showId}: {json ?? "<none>"}",
            LogTarget.All);
    }

    public void UpdateSeriesStatus(long showId, ShowSeriesStatus seriesStatus)
    {
        _databaseService.UpdateTrackedShowSeriesStatus(showId, seriesStatus);
        _logger.Info($"Updated series status for show id={showId}: {seriesStatus}", LogTarget.All);
    }

    public void UpdateWatchProgress(long showId, UserWatchStatus watchStatus, int watchedEpisodes)
    {
        _databaseService.UpdateTrackedShowWatchProgress(showId, watchStatus, watchedEpisodes);
        _logger.Info($"Updated watch progress for show id={showId}: {watchStatus}, episodes={watchedEpisodes}", LogTarget.All);
    }

    public void UpdateRating(long showId, double? rating, string? thought)
    {
        _databaseService.UpdateTrackedShowRating(showId, rating, thought);
        _logger.Info($"Updated rating/thought for show id={showId}: rating={rating?.ToString("0.0") ?? "null"}", LogTarget.All);
    }

    public void UpdateEpisodeRating(long episodeId, double? rating, string? thought)
    {
        _databaseService.UpdateTrackedEpisodeRating(episodeId, rating, thought);
        _logger.Info($"Updated episode rating/thought for episode id={episodeId}: rating={rating?.ToString("0.0") ?? "null"}", LogTarget.All);
    }

    public void SetAlternativeTitleExcludedFromSearch(long showId, string title, bool excluded)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var show = _databaseService.GetTrackedShow(showId)
            ?? throw new InvalidOperationException($"Tracked show id={showId} was not found.");
        var excludedTitles = show.ExcludedFromSearchAlternativeTitles.ToList();
        var normalizedTitle = title.Trim();
        if (excluded)
        {
            if (!excludedTitles.Any(existing => string.Equals(existing, normalizedTitle, StringComparison.OrdinalIgnoreCase)))
            {
                excludedTitles.Add(normalizedTitle);
            }
        }
        else
        {
            excludedTitles.RemoveAll(existing => string.Equals(existing, normalizedTitle, StringComparison.OrdinalIgnoreCase));
        }

        var json = TrackedShow.SerializeAlternativeTitles(excludedTitles);
        _databaseService.UpdateTrackedShowExcludedAlternativeTitles(showId, json);
        _logger.Info(
            $"Alternative title '{normalizedTitle}' {(excluded ? "excluded from" : "included in")} recipe search for show id={showId}.",
            LogTarget.All);
    }

    private long ImportShow(
        TmdbShowDetails details,
        string preferredQuality,
        string? episodeGroupId = null,
        string? episodeGroupName = null)
    {
        var existing = _databaseService.GetTrackedShowByTmdbId(details.TmdbId);

        // First insert uses explicit args. Refresh/switch rely on the row (Switch updates organization first).
        string? resolvedGroupId;
        string? resolvedGroupName;
        if (existing is null)
        {
            resolvedGroupId = string.IsNullOrWhiteSpace(episodeGroupId) ? null : episodeGroupId.Trim();
            resolvedGroupName = resolvedGroupId is null
                ? null
                : (string.IsNullOrWhiteSpace(episodeGroupName) ? "Episode Group" : episodeGroupName.Trim());
        }
        else
        {
            resolvedGroupId = existing.EpisodeGroupId;
            resolvedGroupName = existing.EpisodeGroupName;
        }

        var showId = _databaseService.UpsertTrackedShow(new TrackedShow
        {
            TmdbId = details.TmdbId,
            Title = details.Title,
            FirstAirYear = details.FirstAirYear,
            Overview = details.Overview,
            PosterPath = details.PosterPath,
            AlternativeTitlesJson = TrackedShow.SerializeAlternativeTitles(details.AlternativeTitles),
            ExcludedAlternativeTitlesJson = existing?.ExcludedAlternativeTitlesJson,
            EpisodeGroupId = resolvedGroupId,
            EpisodeGroupName = resolvedGroupName,
            RecipeId = existing?.RecipeId,
            PackRecipeId = existing?.PackRecipeId,
            CartEpisodeOverridesJson = existing?.CartEpisodeOverridesJson,
            CartPackOverridesJson = existing?.CartPackOverridesJson,
            AutoTrackEpisodeOverridesJson = existing?.AutoTrackEpisodeOverridesJson,
            PreferredQuality = existing?.PreferredQuality ?? preferredQuality,
            PreferredAudioCodec = existing?.PreferredAudioCodec ?? string.Empty,
            MinimumSeeders = existing?.MinimumSeeders ?? 0,
            SeriesStatus = details.SeriesStatus,
            PlannedEpisodeCount = details.PlannedEpisodeCount
        });

        // Before the rows are matched to TMDB by number: rows, links and downloads follow their TMDB episode id to
        // the number TMDB gives it now (audit L3-09).
        var identity = existing is null ? null : FollowEpisodeIds(existing, details);

        foreach (var season in details.Seasons)
        {
            _databaseService.UpsertTrackedSeason(new TrackedSeason
            {
                ShowId = showId,
                SeasonNumber = season.SeasonNumber,
                EpisodeCount = season.EpisodeCount,
                DownloadFolder = existing is null
                    ? null
                    : _databaseService.GetTrackedSeasons(existing.Id)
                        .FirstOrDefault(item => item.SeasonNumber == season.SeasonNumber)?.DownloadFolder
            });

            if (season.SeasonNumber == AppConstants.SpecialsSeasonNumber)
            {
                _databaseService.UpdateTrackedSeasonPackMode(showId, season.SeasonNumber, SeasonManagementMode.Episode);
            }

            foreach (var episode in season.Episodes)
            {
                if (identity?.IsPinned((episode.SeasonNumber, episode.EpisodeNumber), episode.TmdbEpisodeId) == true)
                {
                    // That number still holds the file of another episode, or this episode is still at its old
                    // number: writing it here would show one episode's file as another's.
                    continue;
                }

                _databaseService.UpsertTrackedEpisode(new TrackedEpisode
                {
                    ShowId = showId,
                    TmdbEpisodeId = episode.TmdbEpisodeId,
                    SeasonNumber = episode.SeasonNumber,
                    EpisodeNumber = episode.EpisodeNumber,
                    Title = episode.Title,
                    AirDate = episode.AirDate,
                    Overview = episode.Overview,
                    VoteAverage = episode.VoteAverage,
                    StillPath = episode.StillPath
                });
            }
        }

        if (existing is not null)
        {
            FlagPinnedEpisodes(existing, identity!);
            HandleEpisodesRemovedUpstream(showId, details, identity!.Pinned.Keys.ToHashSet());
        }

        return showId;
    }

    /// <summary>
    /// Moves every row whose TMDB episode id now has another number, with its library link, Jellyfin symlink and
    /// download, to that number. Rows that cannot follow (the id is gone, the link cannot move) are left as they are
    /// and returned as pinned. Rows saved before ids were stored have none: the first refresh only fills them.
    /// </summary>
    private EpisodeIdentityPlanner.Plan FollowEpisodeIds(TrackedShow show, TmdbShowDetails details)
    {
        var rows = _databaseService.GetTrackedEpisodes(show.Id);
        var linkedKeys = GetLinkedEpisodeKeys(show.TmdbId.ToString());
        var orderedEpisodeIds = _databaseService.GetTorrentCartOrders(MediaKind.TvEpisode, show.Id)
            .Where(order => order.EpisodeId is not null)
            .Select(order => order.EpisodeId!.Value)
            .ToHashSet();
        bool HoldsDownload(TrackedEpisode row) =>
            linkedKeys.Contains((row.SeasonNumber, row.EpisodeNumber)) ||
            !string.IsNullOrWhiteSpace(row.TorrentHash) ||
            orderedEpisodeIds.Contains(row.Id);
        bool IsInUse(TrackedEpisode row) =>
            HoldsDownload(row) || row.UserRating is not null || !string.IsNullOrWhiteSpace(row.Thought);

        var blocked = new HashSet<(int SeasonNumber, int EpisodeNumber)>();
        while (true)
        {
            var plan = EpisodeIdentityPlanner.Create(rows, details, HoldsDownload, IsInUse, blocked);
            if (plan.Moves.Count == 0)
            {
                foreach (var episodeId in plan.DeleteRowIds)
                {
                    _databaseService.DeleteTrackedEpisode(episodeId);
                }

                return plan;
            }

            var links = _autoTorrentLinkService.RenumberEpisodes(
                show.Id,
                plan.Moves.Select(move => new EpisodeRenumber(move.Row.Id, move.From, move.To)).ToList(),
                plan.DeleteRowIds);
            if (links.Failed || links.BlockedKeys.Count > 0)
            {
                foreach (var message in links.Messages)
                {
                    _logger.Warning($"{show.DisplayTitle}: {message}", LogTarget.All);
                }

                // Every blocked number is a move source, so each pass leaves fewer moves and this ends.
                blocked.UnionWith(links.Failed ? plan.Moves.Select(move => move.From) : links.BlockedKeys);
                continue;
            }

            foreach (var move in plan.Moves)
            {
                _logger.Info(
                    $"TMDB renumbered {show.DisplayTitle} {EpisodeIdentityPlanner.Describe(move.From)} -> {EpisodeIdentityPlanner.Describe(move.To)} " +
                    $"(TMDB episode {move.Row.TmdbEpisodeId}); its row, link and download moved with it.",
                    LogTarget.All);
            }

            return plan;
        }
    }

    private void FlagPinnedEpisodes(TrackedShow show, EpisodeIdentityPlanner.Plan identity)
    {
        var now = DateTime.UtcNow;
        foreach (var (row, reason) in identity.Pinned.Values)
        {
            _databaseService.MarkTrackedEpisodeRemovedUpstream(row.Id, now);
            _logger.Warning(
                $"{show.DisplayTitle} S{row.SeasonNumber:00}E{row.EpisodeNumber:00} (TMDB episode {row.TmdbEpisodeId}): {reason}. " +
                "Its link and download were kept as they are and the episode is flagged.",
                LogTarget.All);
        }
    }

    /// <summary>
    /// Episodes TMDB no longer lists (renumbering, a season split into two) used to stay forever:
    /// an old linked number looked Available while the new number was hunted again (PARSE-31).
    /// Only seasons TMDB returned are checked, so a partial response never removes a whole season.
    /// Unused rows are deleted; rows something still refers to are kept and flagged.
    /// </summary>
    private void HandleEpisodesRemovedUpstream(long showId, TmdbShowDetails details, IReadOnlySet<long> alreadyFlagged)
    {
        var returnedSeasons = details.Seasons
            .Where(season => season.Episodes.Count > 0)
            .Select(season => season.SeasonNumber)
            .ToHashSet();
        if (returnedSeasons.Count == 0)
        {
            return;
        }

        var returnedEpisodes = details.Seasons
            .SelectMany(season => season.Episodes)
            .Select(episode => (episode.SeasonNumber, episode.EpisodeNumber))
            .ToHashSet();
        var stale = _databaseService.GetTrackedEpisodes(showId)
            .Where(episode => returnedSeasons.Contains(episode.SeasonNumber) &&
                              !returnedEpisodes.Contains((episode.SeasonNumber, episode.EpisodeNumber)))
            .ToList();
        if (stale.Count == 0)
        {
            return;
        }

        var show = _databaseService.GetTrackedShow(showId);
        var linkedKeys = GetLinkedEpisodeKeys(show?.TmdbId.ToString());
        var orderedEpisodeIds = _databaseService.GetTorrentCartOrders(MediaKind.TvEpisode, showId)
            .Where(order => order.EpisodeId is not null)
            .Select(order => order.EpisodeId!.Value)
            .ToHashSet();
        var now = DateTime.UtcNow;
        foreach (var episode in stale.Where(episode => !alreadyFlagged.Contains(episode.Id)))
        {
            var inUse = linkedKeys.Contains((episode.SeasonNumber, episode.EpisodeNumber)) ||
                        !string.IsNullOrWhiteSpace(episode.TorrentHash) ||
                        orderedEpisodeIds.Contains(episode.Id) ||
                        episode.UserRating is not null ||
                        !string.IsNullOrWhiteSpace(episode.Thought);
            if (inUse)
            {
                _databaseService.MarkTrackedEpisodeRemovedUpstream(episode.Id, now);
                _logger.Warning(
                    $"TMDB no longer lists {show?.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}; kept because it is linked, downloading or rated.",
                    LogTarget.All);
                continue;
            }

            _databaseService.DeleteTrackedEpisode(episode.Id);
            _logger.Info(
                $"TMDB no longer lists {show?.DisplayTitle} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}; removed the unused episode row.",
                LogTarget.File | LogTarget.Console);
        }
    }

    private HashSet<(int SeasonNumber, int EpisodeNumber)> GetLinkedEpisodeKeys(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return [];
        }

        return _databaseService.GetSourceItems()
            .Where(item =>
                item.MediaKind == MediaKind.TvEpisode &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(GetOutputEpisodeKeys)
            .ToHashSet();
    }

    private Task<TmdbShowDetails> ResolveShowDetailsAsync(
        int tmdbId,
        string? episodeGroupId,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(episodeGroupId)
            ? _catalogService.GetTvShowDetailsAsync(tmdbId, cancellationToken)
            : _catalogService.GetTvShowDetailsByEpisodeGroupAsync(tmdbId, episodeGroupId, cancellationToken);
    }

    private void RefreshAvailabilityCore(long showId, IReadOnlyList<SourceItem> sourceItems)
    {
        var show = _databaseService.GetTrackedShow(showId);
        if (show is null)
        {
            return;
        }

        var availableEpisodes = GetAvailableEpisodeKeys(show.TmdbId, sourceItems);
        foreach (var episode in _databaseService.GetTrackedEpisodes(showId))
        {
            var availability = availableEpisodes.Contains((episode.SeasonNumber, episode.EpisodeNumber))
                ? EpisodeAvailability.Available
                : EpisodeAvailability.Missing;
            if (episode.Availability != availability)
            {
                _databaseService.UpdateTrackedEpisodeAvailability(episode.Id, availability);
            }
        }
    }

    private static HashSet<(int SeasonNumber, int EpisodeNumber)> GetAvailableEpisodeKeys(
        int tmdbId,
        IReadOnlyList<SourceItem> sourceItems)
    {
        var providerId = tmdbId.ToString();
        return sourceItems
            .Where(item =>
                item.MediaKind == MediaKind.TvEpisode &&
                item.MatchAccepted &&
                item.State == ItemState.Linked &&
                string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(GetOutputEpisodeKeys)
            .ToHashSet();
    }

    // A multi-episode file (S01E01-E02) makes every episode of its range available (PARSE-06).
    private static IEnumerable<(int SeasonNumber, int EpisodeNumber)> GetOutputEpisodeKeys(SourceItem item)
    {
        if (GetOutputEpisodeKey(item) is not { } key)
        {
            yield break;
        }

        yield return key;
        if (item.MappedEpisodeNumber is null && item.LastEpisodeNumber is { } last)
        {
            for (var episode = key.EpisodeNumber + 1; episode <= last; episode++)
            {
                yield return (key.SeasonNumber, episode);
            }
        }
    }

    private static (int SeasonNumber, int EpisodeNumber)? GetOutputEpisodeKey(SourceItem item)
    {
        if (item.MappedSeasonNumber is not null && item.MappedEpisodeNumber is not null)
        {
            return (item.MappedSeasonNumber.Value, item.MappedEpisodeNumber.Value);
        }

        if (item.SeasonNumber is not null && item.EpisodeNumber is not null)
        {
            return (item.SeasonNumber.Value, item.EpisodeNumber.Value);
        }

        return null;
    }

    public IReadOnlyList<TrackedShow> GetAutoTrackedShows()
    {
        return _databaseService.GetAutoTrackedShows();
    }

    public void SetAutoTrackCheckpoint(long showId, int fromSeason, int fromEpisode, string? downloadFolder = null, bool autoReconcileAndLink = true)
    {
        _databaseService.UpdateTrackedShowAutoTrackSettings(
            showId,
            fromSeason,
            fromEpisode,
            downloadFolder,
            autoReconcileAndLink);
        _logger.Info(
            $"Auto-track enabled for show {showId} from S{fromSeason:00}E{fromEpisode:00}, folder='{downloadFolder ?? "<default>"}', reconcile={autoReconcileAndLink}.",
            LogTarget.All);
    }

    public void UpdateAutoTrackDownloadFolder(long showId, string? downloadFolder)
    {
        _databaseService.UpdateTrackedShowAutoTrackDownloadFolder(showId, downloadFolder);
        _logger.Info($"Auto-track download folder updated for show {showId}: '{downloadFolder ?? "<cleared>"}'.", LogTarget.All);
    }

    public void UpdateAutoTrackReconcileAndLink(long showId, bool autoReconcileAndLink)
    {
        _databaseService.UpdateTrackedShowAutoTrackReconcileAndLink(showId, autoReconcileAndLink);
        _logger.Info($"Auto-track reconcile/link for show {showId}: {autoReconcileAndLink}.", LogTarget.All);
    }

    public void UpdateAutoTrackScheduleOverrides(long showId, DayOfWeek? anchorDayOfWeek, string? anchorTimeLocal, bool clearOverrides)
    {
        _databaseService.UpdateTrackedShowAutoTrackScheduleOverrides(showId, anchorDayOfWeek, anchorTimeLocal, clearOverrides);
        _logger.Info($"Auto-track schedule overrides updated for show {showId}. Clear={clearOverrides}.", LogTarget.All);
    }

    public void StopAutoTrack(long showId)
    {
        _databaseService.UpdateTrackedShowAutoTrack(showId, null, null);
        _logger.Info($"Auto-track disabled for show {showId}.", LogTarget.All);
    }

    public void ResetAutoTrackWeekSatisfaction(long showId)
    {
        var show = _databaseService.GetTrackedShow(showId);
        if (show is null)
        {
            return;
        }

        _databaseService.UpdateTrackedShowAutoTrackTmdbState(
            showId,
            show.AutoTrackTmdbState,
            lastTmdbWeekKey: null,
            lastTmdbRefreshLocal: null);
        _logger.Info(
            $"Auto-track week satisfaction reset for show {showId} ('{show.DisplayTitle}').",
            LogTarget.File | LogTarget.Console);
    }

    private Task CachePosterAsync(int tmdbId, string? posterPath, CancellationToken cancellationToken)
    {
        return _posterImageService.EnsureCachedAsync(MediaKind.TvEpisode, tmdbId, posterPath, cancellationToken);
    }
}
