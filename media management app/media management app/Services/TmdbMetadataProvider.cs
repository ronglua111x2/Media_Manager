using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class TmdbMetadataProvider : IMetadataProvider
{
    private const double HighConfidenceThreshold = 75;
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly HttpClient _httpClient;

    public TmdbMetadataProvider(ISettingsService settingsService, IAppLogger logger, HttpClient httpClient)
    {
        _settingsService = settingsService;
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    }

    public async Task<MetadataMatchResult> MatchTvSeriesAsync(SourceItem item, CancellationToken cancellationToken = default)
    {
        if (item.MediaKind != MediaKind.TvEpisode || string.IsNullOrWhiteSpace(item.ShowTitle))
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
            var (queryTitle, parsedYear) = SplitTrailingYear(item.ShowTitle);
            var searchPath = $"search/tv?query={Uri.EscapeDataString(queryTitle)}&include_adult=false&language=en-US&page=1";
            using var searchResponse = await _httpClient.GetAsync(searchPath, cancellationToken);
            searchResponse.EnsureSuccessStatusCode();

            await using var searchStream = await searchResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var searchDocument = await JsonDocument.ParseAsync(searchStream, cancellationToken: cancellationToken);
            var searchResults = searchDocument.RootElement.GetProperty("results")
                .EnumerateArray()
                .Take(8)
                .ToList();

            var candidates = new List<TmdbTvCandidate>();
            foreach (var result in searchResults)
            {
                var id = result.GetProperty("id").GetInt32();
                var candidate = await GetCandidateDetailsAsync(id, cancellationToken);
                if (candidate is null)
                {
                    continue;
                }

                ScoreCandidate(candidate, item, queryTitle, parsedYear);
                candidates.Add(candidate);
            }

            candidates = candidates
                .OrderByDescending(candidate => candidate.Confidence)
                .ThenByDescending(candidate => candidate.NumberOfEpisodes ?? 0)
                .ToList();

            var best = candidates.FirstOrDefault();
            _logger.Info(best is null
                ? $"TMDb returned no candidates for '{item.ShowTitle}'"
                : $"TMDb best match for '{item.ShowTitle}' is '{best.Name}' ({best.FirstAirYear}) id={best.Id} confidence={best.Confidence:0}. Reason: {best.MatchReason}",
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
            _logger.Error($"TMDb matching failed for {item.FilePath}", ex, LogTarget.All);
            return new MetadataMatchResult { IsAvailable = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<TmdbTvCandidate?> GetCandidateDetailsAsync(int id, CancellationToken cancellationToken)
    {
        using var detailsResponse = await _httpClient.GetAsync($"tv/{id}?language=en-US", cancellationToken);
        if (!detailsResponse.IsSuccessStatusCode)
        {
            return null;
        }

        await using var detailsStream = await detailsResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var detailsDocument = await JsonDocument.ParseAsync(detailsStream, cancellationToken: cancellationToken);
        var root = detailsDocument.RootElement;
        var firstAirDate = GetString(root, "first_air_date");

        return new TmdbTvCandidate
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
    }

    private static void ScoreCandidate(TmdbTvCandidate candidate, SourceItem item, string queryTitle, int? parsedYear)
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

        if (parsedYear is not null && candidate.FirstAirYear == parsedYear)
        {
            score += 20;
            reasons.Add($"year matched {parsedYear}");
        }

        if (item.ParserPattern == ParserPattern.AnimeAbsolute)
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

        if (item.EpisodeNumber is not null && candidate.NumberOfEpisodes is not null)
        {
            var episode = item.EpisodeNumber.Value;
            var availableEpisodes = candidate.NumberOfEpisodes.Value;
            if (episode <= availableEpisodes + 20)
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

        if (candidate.Id == 37854 && item.ParserPattern == ParserPattern.AnimeAbsolute && normalizedQuery == "one piece")
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

    private void ConfigureHeaders()
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settingsService.Current.TmdbReadAccessToken);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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

    private static string NormalizeTitle(string value)
    {
        return string.Join(' ', value.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
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
}
