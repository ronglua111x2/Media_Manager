using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class MediaImportService : IMediaImportService
{
    private const double AutoAcceptConfidence = 75;

    private readonly IScannerService _scannerService;
    private readonly IMetadataProvider _metadataProvider;
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly IDatabaseService _databaseService;
    private readonly IAppLogger _logger;

    public MediaImportService(
        IScannerService scannerService,
        IMetadataProvider metadataProvider,
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        IDatabaseService databaseService,
        IAppLogger logger)
    {
        _scannerService = scannerService;
        _metadataProvider = metadataProvider;
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task<MediaImportPreviewResult> PreviewAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        var sourceItems = _scannerService.Scan(folders).ToList();
        var groups = new Dictionary<string, MediaImportPreviewGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in sourceItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = item.MediaKind switch
            {
                MediaKind.TvEpisode => await BuildTvGroupAsync(item, cancellationToken),
                MediaKind.Movie => await BuildMovieGroupAsync(item, cancellationToken),
                _ => BuildUnresolvedGroup(item, item.ParserPattern == ParserPattern.Ignored
                    ? MediaImportGroupStatus.Ignored
                    : MediaImportGroupStatus.NeedsReview)
            };

            var key = BuildGroupKey(group);
            if (!groups.TryGetValue(key, out var existingGroup))
            {
                groups[key] = group;
                continue;
            }

            existingGroup.Items.AddRange(group.Items);
            existingGroup.Status = MergeStatus(existingGroup.Status, group.Status);
            if (existingGroup.SelectedCandidate is null && group.SelectedCandidate is not null)
            {
                existingGroup.SelectedCandidate = group.SelectedCandidate;
            }

            foreach (var candidate in group.Candidates.Where(candidate =>
                         existingGroup.Candidates.All(existing => existing.TmdbId != candidate.TmdbId)))
            {
                existingGroup.Candidates.Add(candidate);
            }
        }

        var result = new MediaImportPreviewResult
        {
            Groups = groups.Values
                .OrderBy(group => group.Status)
                .ThenBy(group => group.SelectedCandidate?.Title ?? group.ParsedTitle)
                .ToList(),
            ScannedFileCount = sourceItems.Count
        };
        result.ReadyFileCount = result.Groups
            .Where(group => group.Status == MediaImportGroupStatus.Ready)
            .Sum(group => group.Items.Count);
        result.NeedsReviewFileCount = result.Groups
            .Where(group => group.Status == MediaImportGroupStatus.NeedsReview)
            .Sum(group => group.Items.Count);
        result.IgnoredFileCount = result.Groups
            .Where(group => group.Status == MediaImportGroupStatus.Ignored)
            .Sum(group => group.Items.Count);

        _logger.Info($"Existing media import preview complete. {result.Summary}", LogTarget.All);
        return result;
    }

    public async Task<IReadOnlyList<MediaImportCandidate>> SearchCandidatesAsync(
        MediaKind mediaKind,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        if (mediaKind == MediaKind.Movie)
        {
            var movies = await _trackedMovieService.SearchMoviesAsync(query, cancellationToken);
            return movies.Select(movie => new MediaImportCandidate
            {
                MediaKind = MediaKind.Movie,
                TmdbId = movie.TmdbId,
                Title = movie.Title,
                Year = movie.ReleaseYear,
                Confidence = 100,
                MatchReason = "Selected from manual movie search."
            }).ToList();
        }

        var shows = await _trackedShowService.SearchShowsAsync(query, cancellationToken);
        return shows.Select(show => new MediaImportCandidate
        {
            MediaKind = MediaKind.TvEpisode,
            TmdbId = show.TmdbId,
            Title = show.Title,
            Year = show.FirstAirYear,
            Confidence = 100,
            MatchReason = "Selected from manual show search."
        }).ToList();
    }

    public async Task<MediaImportCommitResult> CommitAsync(
        IEnumerable<MediaImportCommitGroup> groups,
        CancellationToken cancellationToken = default)
    {
        var result = new MediaImportCommitResult();
        var importedShowIds = new Dictionary<int, long>();
        var importedMovieIds = new Dictionary<int, long>();

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Items.Count == 0 || group.SelectedCandidate.TmdbId <= 0)
            {
                result.SkippedFileCount += group.Items.Count;
                continue;
            }

            if (group.MediaKind == MediaKind.Movie)
            {
                var movie = await _trackedMovieService.ImportMovieByTmdbIdAsync(group.SelectedCandidate.TmdbId, cancellationToken);
                if (!importedMovieIds.ContainsKey(movie.TmdbId))
                {
                    importedMovieIds[movie.TmdbId] = movie.Id;
                    result.ImportedMovieCount++;
                    result.ImportedMedia.Add((MediaKind.Movie, movie.Id));
                }

                foreach (var item in group.Items)
                {
                    CommitMovieItem(item, group.SelectedCandidate);
                    result.ImportedFileCount++;
                }

                _trackedMovieService.RefreshAvailability(movie.Id);
                continue;
            }

            var show = await _trackedShowService.ImportShowByTmdbIdAsync(group.SelectedCandidate.TmdbId, cancellationToken);
            if (!importedShowIds.ContainsKey(show.TmdbId))
            {
                importedShowIds[show.TmdbId] = show.Id;
                result.ImportedShowCount++;
                result.ImportedMedia.Add((MediaKind.TvEpisode, show.Id));
            }

            foreach (var item in group.Items)
            {
                if (!await PrepareTvItemAsync(item, group.SelectedCandidate, cancellationToken))
                {
                    result.SkippedFileCount++;
                    continue;
                }

                CommitTvItem(item, group.SelectedCandidate);
                result.ImportedFileCount++;
            }

            _trackedShowService.RefreshAvailability(show.Id);
        }

        _logger.Info($"Existing media import commit complete. {result.Summary}", LogTarget.All);
        return result;
    }

    private async Task<MediaImportPreviewGroup> BuildTvGroupAsync(SourceItem item, CancellationToken cancellationToken)
    {
        var match = await _metadataProvider.MatchTvSeriesAsync(item, cancellationToken);
        if (!match.IsAvailable || match.BestCandidate is null)
        {
            item.State = ItemState.NeedsReview;
            item.Notes = match.ErrorMessage ?? "No TV match was found.";
            return BuildUnresolvedGroup(item, MediaImportGroupStatus.NeedsReview);
        }

        var candidates = match.Candidates.Select(ToImportCandidate).ToList();
        var selected = candidates.First(candidate => candidate.TmdbId == match.BestCandidate.Id);
        ApplyCandidate(item, selected);

        var status = selected.Confidence >= AutoAcceptConfidence
            ? MediaImportGroupStatus.Ready
            : MediaImportGroupStatus.NeedsReview;
        item.RequiresManualReview = status == MediaImportGroupStatus.NeedsReview;
        item.State = item.RequiresManualReview ? ItemState.NeedsReview : ItemState.Parsed;
        item.Notes = item.RequiresManualReview ? selected.MatchReason : null;

        if (!item.RequiresManualReview && item.ParserPattern == ParserPattern.AnimeAbsolute)
        {
            var mapping = await _metadataProvider.MapTvEpisodeAsync(item, cancellationToken);
            if (mapping is { IsAvailable: true, IsMapped: true })
            {
                item.MappedSeasonNumber = mapping.SeasonNumber;
                item.MappedEpisodeNumber = mapping.EpisodeNumber;
                item.EpisodeMappingSource = mapping.Source;
                item.EpisodeMappingConfidence = mapping.Confidence;
                item.EpisodeMappingReason = mapping.Reason;
            }
            else
            {
                item.RequiresManualReview = true;
                item.State = ItemState.NeedsReview;
                item.Notes = mapping.ErrorMessage ?? mapping.Reason ?? "Anime absolute episode could not be mapped.";
                status = MediaImportGroupStatus.NeedsReview;
            }
        }

        return new MediaImportPreviewGroup
        {
            MediaKind = MediaKind.TvEpisode,
            ParsedTitle = item.ShowTitle ?? item.DisplayTitle,
            ParsedYear = selected.Year,
            Status = status,
            SelectedCandidate = selected,
            Candidates = candidates,
            Items = { item }
        };
    }

    private async Task<MediaImportPreviewGroup> BuildMovieGroupAsync(SourceItem item, CancellationToken cancellationToken)
    {
        var match = await _metadataProvider.MatchMovieAsync(item, cancellationToken);
        if (!match.IsAvailable || match.BestCandidate is null)
        {
            item.State = ItemState.NeedsReview;
            item.Notes = match.ErrorMessage ?? "No movie match was found.";
            return BuildUnresolvedGroup(item, MediaImportGroupStatus.NeedsReview);
        }

        var candidates = match.Candidates.Select(ToImportCandidate).ToList();
        var selected = candidates.First(candidate => candidate.TmdbId == match.BestCandidate.Id);
        ApplyCandidate(item, selected);

        var status = selected.Confidence >= AutoAcceptConfidence
            ? MediaImportGroupStatus.Ready
            : MediaImportGroupStatus.NeedsReview;
        item.RequiresManualReview = status == MediaImportGroupStatus.NeedsReview;
        item.State = item.RequiresManualReview ? ItemState.NeedsReview : ItemState.Parsed;
        item.Notes = item.RequiresManualReview ? selected.MatchReason : null;

        return new MediaImportPreviewGroup
        {
            MediaKind = MediaKind.Movie,
            ParsedTitle = item.MovieTitle ?? item.DisplayTitle,
            ParsedYear = item.MovieYear,
            Status = status,
            SelectedCandidate = selected,
            Candidates = candidates,
            Items = { item }
        };
    }

    private static MediaImportPreviewGroup BuildUnresolvedGroup(SourceItem item, MediaImportGroupStatus status)
    {
        item.State = status == MediaImportGroupStatus.Ignored ? ItemState.Ignored : ItemState.NeedsReview;
        return new MediaImportPreviewGroup
        {
            MediaKind = item.MediaKind,
            ParsedTitle = item.DisplayTitle,
            ParsedYear = item.MovieYear,
            Status = status,
            Items = { item }
        };
    }

    private async Task<bool> PrepareTvItemAsync(
        SourceItem item,
        MediaImportCandidate candidate,
        CancellationToken cancellationToken)
    {
        ApplyCandidate(item, candidate);
        if (item.ParserPattern != ParserPattern.AnimeAbsolute)
        {
            return true;
        }

        var mapping = await _metadataProvider.MapTvEpisodeAsync(item, cancellationToken);
        if (mapping is not { IsAvailable: true, IsMapped: true })
        {
            _logger.Warning(
                $"Skipping imported anime absolute item because TMDB mapping failed: {item.FilePath}. {mapping.ErrorMessage ?? mapping.Reason}",
                LogTarget.All);
            return false;
        }

        item.MappedSeasonNumber = mapping.SeasonNumber;
        item.MappedEpisodeNumber = mapping.EpisodeNumber;
        item.EpisodeMappingSource = mapping.Source;
        item.EpisodeMappingConfidence = mapping.Confidence;
        item.EpisodeMappingReason = mapping.Reason;
        return true;
    }

    private void CommitTvItem(SourceItem item, MediaImportCandidate candidate)
    {
        ApplyCandidate(item, candidate);
        item.MediaKind = MediaKind.TvEpisode;
        item.LinkedPath = item.FilePath;
        item.IsExternalImport = true;
        item.MatchAccepted = true;
        item.RequiresManualReview = false;
        item.State = ItemState.Linked;
        item.AutoTorrentLinkKind = null;
        item.AutoTorrentTorrentHash = null;
        item.AutoTorrentPackOwnerSeasonNumber = null;
        item.Notes = "Imported from existing hardlinked media.";
        item.LastSeenUtc = DateTime.UtcNow;
        _databaseService.UpdateSourceItem(item);
    }

    private void CommitMovieItem(SourceItem item, MediaImportCandidate candidate)
    {
        ApplyCandidate(item, candidate);
        item.MediaKind = MediaKind.Movie;
        item.MovieTitle = candidate.Title;
        item.MovieYear = candidate.Year;
        item.LinkedPath = item.FilePath;
        item.IsExternalImport = true;
        item.MatchAccepted = true;
        item.RequiresManualReview = false;
        item.State = ItemState.Linked;
        item.AutoTorrentLinkKind = null;
        item.AutoTorrentTorrentHash = null;
        item.AutoTorrentPackOwnerSeasonNumber = null;
        item.Notes = "Imported from existing hardlinked media.";
        item.LastSeenUtc = DateTime.UtcNow;
        _databaseService.UpdateSourceItem(item);
    }

    private static void ApplyCandidate(SourceItem item, MediaImportCandidate candidate)
    {
        item.MatchedTitle = candidate.Title;
        item.MatchedYear = candidate.Year;
        item.Provider = "tmdb";
        item.ProviderId = candidate.TmdbId.ToString();
        item.MatchConfidence = candidate.Confidence;
        item.MatchReason = candidate.MatchReason;
    }

    private static string BuildGroupKey(MediaImportPreviewGroup group)
    {
        if (group.SelectedCandidate is not null)
        {
            return $"{group.MediaKind}:{group.SelectedCandidate.TmdbId}";
        }

        return $"{group.Status}:{group.MediaKind}:{group.ParsedTitle}:{group.ParsedYear}";
    }

    private static MediaImportGroupStatus MergeStatus(MediaImportGroupStatus left, MediaImportGroupStatus right)
    {
        if (left == MediaImportGroupStatus.Ignored && right == MediaImportGroupStatus.Ignored)
        {
            return MediaImportGroupStatus.Ignored;
        }

        if (left == MediaImportGroupStatus.NeedsReview || right == MediaImportGroupStatus.NeedsReview)
        {
            return MediaImportGroupStatus.NeedsReview;
        }

        return MediaImportGroupStatus.Ready;
    }

    private static MediaImportCandidate ToImportCandidate(TmdbTvCandidate candidate)
    {
        return new MediaImportCandidate
        {
            MediaKind = MediaKind.TvEpisode,
            TmdbId = candidate.Id,
            Title = candidate.Name,
            Year = candidate.FirstAirYear,
            Confidence = candidate.Confidence,
            MatchReason = candidate.MatchReason
        };
    }

    private static MediaImportCandidate ToImportCandidate(TmdbMovieCandidate candidate)
    {
        return new MediaImportCandidate
        {
            MediaKind = MediaKind.Movie,
            TmdbId = candidate.Id,
            Title = candidate.Title,
            Year = candidate.ReleaseYear,
            Confidence = candidate.Confidence,
            MatchReason = candidate.MatchReason
        };
    }
}
