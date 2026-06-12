using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class TmdbMetadataProvider : IMetadataProvider, ITmdbShowCatalogService, ITmdbMovieCatalogService
{
    private const double HighConfidenceThreshold = 75;
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<int, TmdbTvCandidate> _candidateDetailsCache = [];
    private readonly Dictionary<string, IReadOnlyList<int>> _tvSearchIdsCache = [];
    private readonly Dictionary<int, IReadOnlyList<TmdbSeasonSummary>> _seasonSummaryCache = [];
    private readonly Dictionary<(int SeriesId, int SeasonNumber), IReadOnlyList<TmdbEpisodeSummary>> _seasonEpisodeCache = [];

    public TmdbMetadataProvider(ISettingsService settingsService, IAppLogger logger, HttpClient httpClient)
    {
        _settingsService = settingsService;
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    }

    public async Task<MetadataMatchResult> MatchTvSeriesAsync(SourceItem item, CancellationToken cancellationToken = default)
    {
        if (item.MediaKind != MediaKind.TvEpisode)
        {
            return new MetadataMatchResult { IsAvailable = false, ErrorMessage = "Item is not a parsed TV episode." };
        }

        return await MatchTvSeriesAsync(TvSeriesMatchRequest.FromSourceItem(item), cancellationToken);
    }

    public async Task<MetadataMatchResult> MatchTvSeriesAsync(TvSeriesMatchRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ShowTitle))
        {
            return new MetadataMatchResult { IsAvailable = false, ErrorMessage = "Item is not a parsed TV episode." };
        }

        if (string.IsNullOrWhiteSpace(_settingsService.Current.TmdbReadAccessToken))
        {
            return new MetadataMatchResult { IsAvailable = false, ErrorMessage = "TMDb read access token is not configured." };
        }

        ConfigureHeaders();

        try
        {
            var (queryTitle, parsedTitleYear) = SplitTrailingYear(request.ShowTitle);
            var yearHints = ExtractYearHints(request, parsedTitleYear);
            var resultIds = await SearchTvSeriesIdsAsync(queryTitle, cancellationToken);

            var candidates = new List<TmdbTvCandidate>();
            foreach (var id in resultIds)
            {
                var candidate = await GetCandidateDetailsAsync(id, cancellationToken);
                if (candidate is null)
                {
                    continue;
                }

                await ScoreCandidateAsync(candidate, request, queryTitle, yearHints, cancellationToken);
                candidates.Add(candidate);
            }

            candidates = candidates
                .OrderByDescending(candidate => candidate.Confidence)
                .ThenByDescending(candidate => candidate.NumberOfEpisodes ?? 0)
                .ThenBy(candidate => candidate.FirstAirYear ?? int.MaxValue)
                .ToList();

            var best = candidates.FirstOrDefault();
            _logger.Info(best is null
                ? $"TMDb returned no candidates for '{request.ShowTitle}'"
                : $"TMDb best match for '{request.ShowTitle}' is '{best.Name}' ({best.FirstAirYear}) id={best.Id} confidence={best.Confidence:0}. Reason: {best.MatchReason}",
                LogTarget.File | LogTarget.Ui | LogTarget.Console);

            return new MetadataMatchResult
            {
                IsAvailable = true,
                Candidates = candidates,
                BestCandidate = best
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"TMDb matching failed for {request.FilePath ?? request.ShowTitle}", ex, LogTarget.All);
            return new MetadataMatchResult { IsAvailable = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<MetadataValidationResult> ValidateTvSeriesMatchAsync(TvSeriesMatchRequest request, string providerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ShowTitle) ||
            string.IsNullOrWhiteSpace(providerId) ||
            !int.TryParse(providerId, NumberStyles.None, CultureInfo.InvariantCulture, out var seriesId))
        {
            return new MetadataValidationResult
            {
                IsAvailable = false,
                ErrorMessage = "TV validation requires a parsed title and numeric provider id."
            };
        }

        if (string.IsNullOrWhiteSpace(_settingsService.Current.TmdbReadAccessToken))
        {
            return new MetadataValidationResult
            {
                IsAvailable = false,
                ErrorMessage = "TMDb read access token is not configured."
            };
        }

        ConfigureHeaders();

        try
        {
            var candidate = await GetCandidateDetailsAsync(seriesId, cancellationToken);
            if (candidate is null)
            {
                return new MetadataValidationResult
                {
                    IsAvailable = true,
                    IsValid = false,
                    Reason = $"TMDb series id {providerId} is no longer available."
                };
            }

            var (queryTitle, parsedTitleYear) = SplitTrailingYear(request.ShowTitle);
            var yearHints = ExtractYearHints(request, parsedTitleYear);
            await ScoreCandidateAsync(candidate, request, queryTitle, yearHints, cancellationToken);

            return new MetadataValidationResult
            {
                IsAvailable = true,
                IsValid = candidate.Confidence >= HighConfidenceThreshold,
                Confidence = candidate.Confidence,
                Reason = candidate.MatchReason
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"TMDb validation failed for {request.FilePath ?? request.ShowTitle}", ex, LogTarget.All);
            return new MetadataValidationResult { IsAvailable = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<MovieMetadataMatchResult> MatchMovieAsync(SourceItem item, CancellationToken cancellationToken = default)
    {
        if (item.MediaKind != MediaKind.Movie)
        {
            return new MovieMetadataMatchResult { IsAvailable = false, ErrorMessage = "Item is not a parsed movie." };
        }

        if (string.IsNullOrWhiteSpace(item.MovieTitle))
        {
            return new MovieMetadataMatchResult { IsAvailable = false, ErrorMessage = "Movie title was not parsed." };
        }

        try
        {
            var results = await SearchMoviesAsync(item.MovieTitle, cancellationToken);
            var queryTitle = NormalizeTitle(item.MovieTitle);
            var candidates = results
                .Select(result => ScoreMovieCandidate(result, queryTitle, item.MovieYear))
                .OrderByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.ReleaseYear ?? int.MaxValue)
                .ToList();

            var best = candidates.FirstOrDefault();
            _logger.Info(best is null
                ? $"TMDb returned no movie candidates for '{item.MovieTitle}'"
                : $"TMDb best movie match for '{item.MovieTitle}' is '{best.DisplayTitle}' id={best.Id} confidence={best.Confidence:0}. Reason: {best.MatchReason}",
                LogTarget.File | LogTarget.Ui | LogTarget.Console);

            return new MovieMetadataMatchResult
            {
                IsAvailable = true,
                Candidates = candidates,
                BestCandidate = best
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"TMDb movie matching failed for {item.FilePath}", ex, LogTarget.All);
            return new MovieMetadataMatchResult { IsAvailable = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<IReadOnlyList<TmdbShowSearchResult>> SearchTvShowsAsync(string query, CancellationToken cancellationToken = default)
    {
        var searchResults = await SearchTvShowsLightweightAsync(query, cancellationToken);
        foreach (var result in searchResults)
        {
            await PopulateShowCountsAsync(result, cancellationToken);
        }

        return searchResults;
    }

    public async Task<IReadOnlyList<TmdbShowSearchResult>> SearchTvShowsLightweightAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(_settingsService.Current.TmdbReadAccessToken))
        {
            throw new InvalidOperationException("TMDb read access token is not configured.");
        }

        ConfigureHeaders();
        var searchPath = $"search/tv?query={Uri.EscapeDataString(query.Trim())}&include_adult=false&language=en-US&page=1";
        using var response = await GetAsyncWithRetryAsync(searchPath, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var searchResults = results.EnumerateArray()
            .Take(12)
            .Select(result =>
            {
                var firstAirDate = GetString(result, "first_air_date");
                return new TmdbShowSearchResult
                {
                    TmdbId = GetInt(result, "id") ?? 0,
                    Title = GetString(result, "name") ?? string.Empty,
                    FirstAirYear = ParseYear(firstAirDate),
                    Overview = GetString(result, "overview"),
                    PosterPath = GetString(result, "poster_path")
                };
            })
            .Where(result => result.TmdbId > 0 && !string.IsNullOrWhiteSpace(result.Title))
            .ToList();

        return searchResults;
    }

    public async Task<TmdbShowDetails> GetTvShowSummaryAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settingsService.Current.TmdbReadAccessToken))
        {
            throw new InvalidOperationException("TMDb read access token is not configured.");
        }

        ConfigureHeaders();
        using var detailsResponse = await GetAsyncWithRetryAsync($"tv/{tmdbId}?language=en-US", cancellationToken);
        detailsResponse.EnsureSuccessStatusCode();

        await using var detailsStream = await detailsResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var detailsDocument = await JsonDocument.ParseAsync(detailsStream, cancellationToken: cancellationToken);
        var root = detailsDocument.RootElement;
        var firstAirDate = GetString(root, "first_air_date");
        return new TmdbShowDetails
        {
            TmdbId = tmdbId,
            Title = GetString(root, "name") ?? string.Empty,
            FirstAirYear = ParseYear(firstAirDate),
            Overview = GetString(root, "overview"),
            PosterPath = GetString(root, "poster_path"),
            SeasonCount = GetInt(root, "number_of_seasons") ?? 0,
            EpisodeCount = GetInt(root, "number_of_episodes") ?? 0
        };
    }

    public async Task<TmdbShowDetails> GetTvShowDetailsAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settingsService.Current.TmdbReadAccessToken))
        {
            throw new InvalidOperationException("TMDb read access token is not configured.");
        }

        ConfigureHeaders();
        using var detailsResponse = await GetAsyncWithRetryAsync($"tv/{tmdbId}?language=en-US", cancellationToken);
        detailsResponse.EnsureSuccessStatusCode();

        await using var detailsStream = await detailsResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var detailsDocument = await JsonDocument.ParseAsync(detailsStream, cancellationToken: cancellationToken);
        var root = detailsDocument.RootElement;
        var firstAirDate = GetString(root, "first_air_date");
        var details = new TmdbShowDetails
        {
            TmdbId = tmdbId,
            Title = GetString(root, "name") ?? string.Empty,
            FirstAirYear = ParseYear(firstAirDate),
            Overview = GetString(root, "overview"),
            PosterPath = GetString(root, "poster_path"),
            SeasonCount = GetInt(root, "number_of_seasons") ?? 0,
            EpisodeCount = GetInt(root, "number_of_episodes") ?? 0
        };

        if (!root.TryGetProperty("seasons", out var seasonsElement) || seasonsElement.ValueKind != JsonValueKind.Array)
        {
            return details;
        }

        var today = DateTime.Today;
        foreach (var seasonElement in seasonsElement.EnumerateArray())
        {
            var seasonNumber = GetInt(seasonElement, "season_number") ?? 0;
            if (seasonNumber <= 0)
            {
                continue;
            }

            var season = await GetTrackedSeasonDetailsAsync(tmdbId, seasonNumber, today, cancellationToken);
            if (season.Episodes.Count > 0)
            {
                details.Seasons.Add(season);
            }
        }

        return details;
    }

    public async Task<IReadOnlyList<TmdbMovieSearchResult>> SearchMoviesAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(_settingsService.Current.TmdbReadAccessToken))
        {
            throw new InvalidOperationException("TMDb read access token is not configured.");
        }

        ConfigureHeaders();
        var searchPath = $"search/movie?query={Uri.EscapeDataString(query.Trim())}&include_adult=false&language=en-US&page=1";
        using var response = await GetAsyncWithRetryAsync(searchPath, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return results.EnumerateArray()
            .Take(12)
            .Select(result =>
            {
                var releaseDate = GetString(result, "release_date");
                return new TmdbMovieSearchResult
                {
                    TmdbId = GetInt(result, "id") ?? 0,
                    Title = GetString(result, "title") ?? string.Empty,
                    ReleaseYear = ParseYear(releaseDate),
                    Overview = GetString(result, "overview"),
                    PosterPath = GetString(result, "poster_path")
                };
            })
            .Where(result => result.TmdbId > 0 && !string.IsNullOrWhiteSpace(result.Title))
            .ToList();
    }

    public async Task<TmdbMovieDetails> GetMovieDetailsAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settingsService.Current.TmdbReadAccessToken))
        {
            throw new InvalidOperationException("TMDb read access token is not configured.");
        }

        ConfigureHeaders();
        using var response = await GetAsyncWithRetryAsync($"movie/{tmdbId}?language=en-US", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var releaseDate = GetString(root, "release_date");
        return new TmdbMovieDetails
        {
            TmdbId = tmdbId,
            Title = GetString(root, "title") ?? string.Empty,
            ReleaseYear = ParseYear(releaseDate),
            Overview = GetString(root, "overview"),
            PosterPath = GetString(root, "poster_path"),
            RuntimeMinutes = GetInt(root, "runtime")
        };
    }

    private static TmdbMovieCandidate ScoreMovieCandidate(TmdbMovieSearchResult result, string normalizedQuery, int? yearHint)
    {
        var normalizedTitle = NormalizeTitle(result.Title);
        var score = 0d;
        var reasons = new List<string>();

        if (normalizedTitle == normalizedQuery)
        {
            score += 55;
            reasons.Add("exact title match");
        }
        else if (normalizedTitle.Contains(normalizedQuery) || normalizedQuery.Contains(normalizedTitle))
        {
            score += 30;
            reasons.Add("partial title match");
        }

        if (yearHint is not null && result.ReleaseYear == yearHint)
        {
            score += 40;
            reasons.Add($"year matched {yearHint}");
        }
        else if (yearHint is not null && result.ReleaseYear is not null)
        {
            score -= 25;
            reasons.Add($"year mismatch; parsed={yearHint}, tmdb={result.ReleaseYear}");
        }
        else if (yearHint is null)
        {
            reasons.Add("no parsed year");
        }

        var confidence = Math.Clamp(score, 0, 100);
        if (confidence < HighConfidenceThreshold)
        {
            reasons.Add("requires manual review");
        }

        return new TmdbMovieCandidate
        {
            Id = result.TmdbId,
            Title = result.Title,
            ReleaseYear = result.ReleaseYear,
            Overview = result.Overview,
            PosterPath = result.PosterPath,
            Confidence = confidence,
            MatchReason = reasons.Count == 0 ? "No strong match signals" : string.Join("; ", reasons)
        };
    }

    public async Task<EpisodeMappingResult> MapTvEpisodeAsync(SourceItem item, CancellationToken cancellationToken = default)
    {
        if (item.MediaKind != MediaKind.TvEpisode ||
            item.EpisodeNumber is null ||
            string.IsNullOrWhiteSpace(item.ProviderId) ||
            !int.TryParse(item.ProviderId, NumberStyles.None, CultureInfo.InvariantCulture, out var seriesId))
        {
            return new EpisodeMappingResult
            {
                IsAvailable = false,
                ErrorMessage = "Item does not have enough TV metadata to map an episode."
            };
        }

        if (string.IsNullOrWhiteSpace(_settingsService.Current.TmdbReadAccessToken))
        {
            return new EpisodeMappingResult
            {
                IsAvailable = false,
                ErrorMessage = "TMDb read access token is not configured."
            };
        }

        ConfigureHeaders();

        try
        {
            var targetEpisode = item.EpisodeNumber.Value;
            var seasons = await GetSeasonSummariesAsync(seriesId, cancellationToken);
            if (seasons.Count == 0)
            {
                return new EpisodeMappingResult
                {
                    IsAvailable = true,
                    IsMapped = false,
                    Reason = "TMDb returned no seasons for this series."
                };
            }

            foreach (var season in seasons)
            {
                var episodes = await GetSeasonEpisodesAsync(seriesId, season.SeasonNumber, cancellationToken);
                var directMatch = episodes.FirstOrDefault(episode => episode.EpisodeNumber == targetEpisode);
                if (directMatch is not null)
                {
                    var reason = $"TMDb direct season lookup found absolute episode {targetEpisode} in season {season.SeasonNumber} ({season.Name}).";
                    _logger.Info($"Mapped {item.ShowTitle} absolute episode {targetEpisode} to S{season.SeasonNumber:00}E{directMatch.EpisodeNumber:00}. {reason}", LogTarget.All);
                    return new EpisodeMappingResult
                    {
                        IsAvailable = true,
                        IsMapped = true,
                        SeasonNumber = season.SeasonNumber,
                        EpisodeNumber = directMatch.EpisodeNumber,
                        Source = "TmdbSeasonDirect",
                        Confidence = 100,
                        Reason = reason
                    };
                }
            }

            var remainingEpisode = targetEpisode;
            foreach (var season in seasons)
            {
                if (season.EpisodeCount <= 0)
                {
                    continue;
                }

                if (remainingEpisode <= season.EpisodeCount)
                {
                    var reason = $"TMDb cumulative season counts mapped absolute episode {targetEpisode} to season {season.SeasonNumber}, episode {remainingEpisode}.";
                    _logger.Warning($"Mapped {item.ShowTitle} absolute episode {targetEpisode} with lower-confidence cumulative fallback: S{season.SeasonNumber:00}E{remainingEpisode:00}", LogTarget.All);
                    return new EpisodeMappingResult
                    {
                        IsAvailable = true,
                        IsMapped = true,
                        SeasonNumber = season.SeasonNumber,
                        EpisodeNumber = remainingEpisode,
                        Source = "TmdbSeasonCumulative",
                        Confidence = 70,
                        Reason = reason
                    };
                }

                remainingEpisode -= season.EpisodeCount;
            }

            return new EpisodeMappingResult
            {
                IsAvailable = true,
                IsMapped = false,
                Reason = $"Absolute episode {targetEpisode} was not found in TMDb seasons."
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"TMDb episode mapping failed for {item.FilePath}", ex, LogTarget.All);
            return new EpisodeMappingResult { IsAvailable = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<TmdbTvCandidate?> GetCandidateDetailsAsync(int id, CancellationToken cancellationToken)
    {
        if (_candidateDetailsCache.TryGetValue(id, out var cached))
        {
            return CloneCandidate(cached);
        }

        using var detailsResponse = await GetAsyncWithRetryAsync($"tv/{id}?language=en-US", cancellationToken);
        if (!detailsResponse.IsSuccessStatusCode)
        {
            return null;
        }

        await using var detailsStream = await detailsResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var detailsDocument = await JsonDocument.ParseAsync(detailsStream, cancellationToken: cancellationToken);
        var root = detailsDocument.RootElement;
        var firstAirDate = GetString(root, "first_air_date");

        var candidate = new TmdbTvCandidate
        {
            Id = id,
            Name = GetString(root, "name") ?? string.Empty,
            OriginalName = GetString(root, "original_name"),
            FirstAirDate = firstAirDate,
            FirstAirYear = ParseYear(firstAirDate),
            OriginalLanguage = GetString(root, "original_language"),
            NumberOfEpisodes = GetInt(root, "number_of_episodes"),
            NumberOfSeasons = GetInt(root, "number_of_seasons"),
            Genres = root.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array
                ? genres.EnumerateArray().Select(genre => GetString(genre, "name")).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().ToList()
                : []
        };

        _candidateDetailsCache[id] = candidate;
        return CloneCandidate(candidate);
    }

    private async Task<IReadOnlyList<int>> SearchTvSeriesIdsAsync(string queryTitle, CancellationToken cancellationToken)
    {
        var cacheKey = NormalizeTitle(queryTitle);
        if (_tvSearchIdsCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var searchPath = $"search/tv?query={Uri.EscapeDataString(queryTitle)}&include_adult=false&language=en-US&page=1";
        using var searchResponse = await GetAsyncWithRetryAsync(searchPath, cancellationToken);
        searchResponse.EnsureSuccessStatusCode();

        await using var searchStream = await searchResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var searchDocument = await JsonDocument.ParseAsync(searchStream, cancellationToken: cancellationToken);
        var resultIds = searchDocument.RootElement.GetProperty("results")
            .EnumerateArray()
            .Take(8)
            .Select(result => result.GetProperty("id").GetInt32())
            .Distinct()
            .ToList();

        _tvSearchIdsCache[cacheKey] = resultIds;
        return resultIds;
    }

    private async Task<IReadOnlyList<TmdbSeasonSummary>> GetSeasonSummariesAsync(int seriesId, CancellationToken cancellationToken)
    {
        if (_seasonSummaryCache.TryGetValue(seriesId, out var cached))
        {
            return cached;
        }

        using var detailsResponse = await GetAsyncWithRetryAsync($"tv/{seriesId}?language=en-US", cancellationToken);
        detailsResponse.EnsureSuccessStatusCode();

        await using var detailsStream = await detailsResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var detailsDocument = await JsonDocument.ParseAsync(detailsStream, cancellationToken: cancellationToken);
        var root = detailsDocument.RootElement;

        var seasons = root.TryGetProperty("seasons", out var seasonsElement) && seasonsElement.ValueKind == JsonValueKind.Array
            ? seasonsElement.EnumerateArray()
                .Select(ReadSeasonSummary)
                .Where(season => season.SeasonNumber > 0)
                .OrderBy(season => season.SeasonNumber)
                .ToList()
            : [];

        _seasonSummaryCache[seriesId] = seasons;
        return seasons;
    }

    private async Task<IReadOnlyList<TmdbEpisodeSummary>> GetSeasonEpisodesAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken)
    {
        var key = (seriesId, seasonNumber);
        if (_seasonEpisodeCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        using var seasonResponse = await GetAsyncWithRetryAsync($"tv/{seriesId}/season/{seasonNumber}?language=en-US", cancellationToken);
        if (!seasonResponse.IsSuccessStatusCode)
        {
            _logger.Warning($"TMDb season lookup failed for series {seriesId}, season {seasonNumber}: {(int)seasonResponse.StatusCode}", LogTarget.File | LogTarget.Console);
            _seasonEpisodeCache[key] = [];
            return [];
        }

        await using var seasonStream = await seasonResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var seasonDocument = await JsonDocument.ParseAsync(seasonStream, cancellationToken: cancellationToken);
        var root = seasonDocument.RootElement;

        var episodes = root.TryGetProperty("episodes", out var episodesElement) && episodesElement.ValueKind == JsonValueKind.Array
            ? episodesElement.EnumerateArray()
                .Select(ReadEpisodeSummary)
                .Where(episode => episode.EpisodeNumber > 0)
                .OrderBy(episode => episode.EpisodeNumber)
                .ToList()
            : [];

        _seasonEpisodeCache[key] = episodes;
        return episodes;
    }

    private async Task<TmdbSeasonDetails> GetTrackedSeasonDetailsAsync(int seriesId, int seasonNumber, DateTime today, CancellationToken cancellationToken)
    {
        using var seasonResponse = await GetAsyncWithRetryAsync($"tv/{seriesId}/season/{seasonNumber}?language=en-US", cancellationToken);
        if (!seasonResponse.IsSuccessStatusCode)
        {
            _logger.Warning($"TMDb tracked season lookup failed for series {seriesId}, season {seasonNumber}: {(int)seasonResponse.StatusCode}", LogTarget.File | LogTarget.Console);
            return new TmdbSeasonDetails { SeasonNumber = seasonNumber };
        }

        await using var seasonStream = await seasonResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var seasonDocument = await JsonDocument.ParseAsync(seasonStream, cancellationToken: cancellationToken);
        var root = seasonDocument.RootElement;
        var details = new TmdbSeasonDetails { SeasonNumber = seasonNumber };

        if (!root.TryGetProperty("episodes", out var episodesElement) || episodesElement.ValueKind != JsonValueKind.Array)
        {
            return details;
        }

        foreach (var episodeElement in episodesElement.EnumerateArray())
        {
            var episodeNumber = GetInt(episodeElement, "episode_number") ?? 0;
            var airDate = ParseDate(GetString(episodeElement, "air_date"));
            if (episodeNumber <= 0 || airDate is null || airDate.Value.Date > today)
            {
                continue;
            }

            details.Episodes.Add(new TmdbEpisodeDetails
            {
                SeasonNumber = seasonNumber,
                EpisodeNumber = episodeNumber,
                Title = GetString(episodeElement, "name") ?? $"Episode {episodeNumber}",
                AirDate = airDate
            });
        }

        details.EpisodeCount = details.Episodes.Count;
        return details;
    }

    private async Task PopulateShowCountsAsync(TmdbShowSearchResult result, CancellationToken cancellationToken)
    {
        using var detailsResponse = await GetAsyncWithRetryAsync($"tv/{result.TmdbId}?language=en-US", cancellationToken);
        if (!detailsResponse.IsSuccessStatusCode)
        {
            return;
        }

        await using var detailsStream = await detailsResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var detailsDocument = await JsonDocument.ParseAsync(detailsStream, cancellationToken: cancellationToken);
        var root = detailsDocument.RootElement;
        result.SeasonCount = GetInt(root, "number_of_seasons") ?? 0;
        result.EpisodeCount = GetInt(root, "number_of_episodes") ?? 0;
    }

    private async Task ScoreCandidateAsync(
        TmdbTvCandidate candidate,
        TvSeriesMatchRequest request,
        string queryTitle,
        IReadOnlySet<int> yearHints,
        CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        var score = 0d;
        var normalizedQuery = NormalizeTitle(queryTitle);
        var normalizedName = NormalizeTitle(candidate.Name);
        var normalizedOriginalName = NormalizeTitle(candidate.OriginalName ?? string.Empty);

        if (normalizedName == normalizedQuery || normalizedOriginalName == normalizedQuery)
        {
            score += 35;
            reasons.Add("exact title match");
        }
        else if (normalizedName.Contains(normalizedQuery) || normalizedQuery.Contains(normalizedName))
        {
            score += 22;
            reasons.Add("partial title match");
        }

        if (candidate.FirstAirYear is not null && yearHints.Contains(candidate.FirstAirYear.Value))
        {
            score += 30;
            reasons.Add($"year hint matched {candidate.FirstAirYear}");
        }
        else if (candidate.FirstAirYear is not null && yearHints.Count > 0)
        {
            score -= 20;
            reasons.Add($"year hint mismatch; hints={string.Join(", ", yearHints)}");
        }

        if (request.ParserPattern == ParserPattern.AnimeAbsolute)
        {
            if (string.Equals(candidate.OriginalLanguage, "ja", StringComparison.OrdinalIgnoreCase))
            {
                score += 18;
                reasons.Add("Japanese original language");
            }

            if (candidate.Genres.Any(genre => string.Equals(genre, "Animation", StringComparison.OrdinalIgnoreCase)))
            {
                score += 22;
                reasons.Add("Animation genre");
            }
            else
            {
                score -= 20;
                reasons.Add("not marked as animation");
            }
        }

        if (request.SeasonNumber is not null && candidate.NumberOfSeasons is not null)
        {
            var season = request.SeasonNumber.Value;
            var availableSeasons = candidate.NumberOfSeasons.Value;
            if (season <= availableSeasons)
            {
                score += 15;
                reasons.Add($"season count plausible ({availableSeasons} seasons)");
            }
            else
            {
                score -= 80;
                reasons.Add($"parsed season {season} exceeds candidate season count {availableSeasons}");
            }
        }

        if (request.EpisodeNumber is not null && candidate.NumberOfEpisodes is not null)
        {
            var episode = request.EpisodeNumber.Value;
            var availableEpisodes = candidate.NumberOfEpisodes.Value;
            if (episode <= availableEpisodes)
            {
                score += episode > 100 ? 25 : 10;
                reasons.Add($"episode count plausible ({availableEpisodes} episodes)");
            }
            else if (episode > availableEpisodes + 100)
            {
                score -= 60;
                reasons.Add($"episode {episode} exceeds candidate episode count {availableEpisodes}");
            }
            else
            {
                score -= 10;
                reasons.Add($"episode {episode} slightly exceeds candidate episode count {availableEpisodes}");
            }
        }

        if (request.ParserPattern == ParserPattern.StandardTv &&
            request.SeasonNumber is not null &&
            request.EpisodeNumber is not null)
        {
            var seasonValidation = await ValidateStandardEpisodeAsync(
                candidate.Id,
                request.SeasonNumber.Value,
                request.EpisodeNumber.Value,
                cancellationToken);

            score += seasonValidation.ScoreDelta;
            reasons.Add(seasonValidation.Reason);
        }

        if (candidate.Id == 37854 && request.ParserPattern == ParserPattern.AnimeAbsolute && normalizedQuery == "one piece")
        {
            score += 10;
            reasons.Add("known One Piece anime candidate");
        }

        candidate.Confidence = Math.Clamp(score, 0, 100);
        candidate.MatchReason = reasons.Count == 0 ? "No strong match signals" : string.Join("; ", reasons);

        if (candidate.Confidence < HighConfidenceThreshold)
        {
            candidate.MatchReason += "; requires manual review";
        }
    }

    private async Task<(double ScoreDelta, string Reason)> ValidateStandardEpisodeAsync(
        int seriesId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken)
    {
        var episodes = await GetSeasonEpisodesAsync(seriesId, seasonNumber, cancellationToken);
        if (episodes.Count == 0)
        {
            return (-40, $"TMDb season {seasonNumber:00} has no episode data");
        }

        if (episodes.Any(episode => episode.EpisodeNumber == episodeNumber))
        {
            return (30, $"TMDb confirms S{seasonNumber:00}E{episodeNumber:00}");
        }

        var maxEpisode = episodes.Max(episode => episode.EpisodeNumber);
        var penalty = episodeNumber > maxEpisode ? -80 : -35;
        return (penalty, $"TMDb season {seasonNumber:00} does not contain episode {episodeNumber:00}; max episode is {maxEpisode:00}");
    }

    private void ConfigureHeaders()
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settingsService.Current.TmdbReadAccessToken);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task<HttpResponseMessage> GetAsyncWithRetryAsync(string requestUri, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await _httpClient.GetAsync(requestUri, cancellationToken);
                if (!ShouldRetry(response.StatusCode) || attempt == maxAttempts)
                {
                    return response;
                }

                _logger.Warning($"TMDb returned {(int)response.StatusCode}; retrying attempt {attempt + 1}/{maxAttempts}.", LogTarget.File | LogTarget.Console);
                response.Dispose();
            }
            catch (Exception ex) when (IsTransientNetworkError(ex) && attempt < maxAttempts)
            {
                _logger.Warning($"TMDb network error; retrying attempt {attempt + 1}/{maxAttempts}. {ex.Message}", LogTarget.File | LogTarget.Console);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(350 * attempt), cancellationToken);
        }

        throw new HttpRequestException("TMDb request failed after retry attempts.");
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
            (int)statusCode >= 500;
    }

    private static bool IsTransientNetworkError(Exception exception)
    {
        return exception is HttpRequestException or IOException;
    }

    private static (string Title, int? Year) SplitTrailingYear(string title)
    {
        var parts = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 1 &&
            int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
            year is >= 1900 and <= 2100)
        {
            parts.RemoveAt(parts.Count - 1);
            return (string.Join(' ', parts), year);
        }

        return (title, null);
    }

    private static IReadOnlySet<int> ExtractYearHints(TvSeriesMatchRequest request, int? parsedTitleYear)
    {
        var years = new HashSet<int>();
        if (parsedTitleYear is not null)
        {
            years.Add(parsedTitleYear.Value);
        }

        AddYears(request.FileName, years);
        AddYears(request.ParentFolder, years);
        AddYears(request.ScanText, years);
        AddYears(request.FilePath, years);
        return years;
    }

    private static void AddYears(string? value, ISet<int> years)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (Match match in Regex.Matches(value, @"(?<!\d)(?:19|20)\d{2}(?!\d)"))
        {
            if (int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            {
                years.Add(year);
            }
        }
    }

    private static string NormalizeTitle(string value)
    {
        value = Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", " ");
        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static TmdbTvCandidate CloneCandidate(TmdbTvCandidate candidate)
    {
        return new TmdbTvCandidate
        {
            Id = candidate.Id,
            Name = candidate.Name,
            OriginalName = candidate.OriginalName,
            FirstAirDate = candidate.FirstAirDate,
            FirstAirYear = candidate.FirstAirYear,
            OriginalLanguage = candidate.OriginalLanguage,
            NumberOfEpisodes = candidate.NumberOfEpisodes,
            NumberOfSeasons = candidate.NumberOfSeasons,
            Genres = candidate.Genres.ToList()
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;
    }

    private static int? ParseYear(string? date)
    {
        return !string.IsNullOrWhiteSpace(date) && date.Length >= 4 && int.TryParse(date[..4], out var year)
            ? year
            : null;
    }

    private static DateTime? ParseDate(string? date)
    {
        return !string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed)
            ? parsed.Date
            : null;
    }

    private static TmdbSeasonSummary ReadSeasonSummary(JsonElement element)
    {
        return new TmdbSeasonSummary
        {
            SeasonNumber = GetInt(element, "season_number") ?? 0,
            EpisodeCount = GetInt(element, "episode_count") ?? 0,
            Name = GetString(element, "name") ?? "Season"
        };
    }

    private static TmdbEpisodeSummary ReadEpisodeSummary(JsonElement element)
    {
        return new TmdbEpisodeSummary
        {
            EpisodeNumber = GetInt(element, "episode_number") ?? 0,
            Name = GetString(element, "name")
        };
    }

    private sealed class TmdbSeasonSummary
    {
        public int SeasonNumber { get; init; }

        public int EpisodeCount { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    private sealed class TmdbEpisodeSummary
    {
        public int EpisodeNumber { get; init; }

        public string? Name { get; init; }
    }
}
