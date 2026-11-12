using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services.Gemini;

public sealed class GeminiModelCatalogService : IGeminiModelCatalogService
{
    private static readonly JsonSerializerOptions FileJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly IAppLogger _logger;
    private GeminiModelCatalogFile _current = GeminiModelCatalogDefaults.CreateSeed();

    public GeminiModelCatalogService(
        ISettingsService settingsService,
        HttpClient httpClient,
        IAppLogger logger)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
        _logger = logger;
        ReloadFromDisk();
    }

    public GeminiModelCatalogFile Current => _current;

    public string CatalogFilePath =>
        Path.Combine(_settingsService.Current.StateFolder, AppConstants.GeminiModelsFileName);

    public string DefaultModel => string.IsNullOrWhiteSpace(_current.DefaultModel)
        ? AppConstants.DefaultGeminiModel
        : _current.DefaultModel;

    public IReadOnlyList<string> FallbackModels =>
        _current.FallbackModels.Count > 0
            ? _current.FallbackModels
            : GeminiModelCatalogDefaults.CreateSeed().FallbackModels;

    public IReadOnlyList<GeminiModelEntry> GetModelsForUi() =>
        _current.Models
            .OrderBy(entry => entry.Deprecated)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<GeminiModelEntry> GetTextMappingModelsForUi() =>
        GetModelsForUi()
            .Where(GeminiTextModelFilter.IsEligibleForFallbackPicker)
            .ToList();

    public string Normalize(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return DefaultModel;
        }

        var trimmed = modelId.Trim();
        var entry = FindEntry(trimmed);
        if (entry is not null)
        {
            if (entry.Deprecated && !string.IsNullOrWhiteSpace(entry.Replacement))
            {
                return Normalize(entry.Replacement);
            }

            return entry.Id;
        }

        // R7: a model the catalog does not know is kept (it may be newer than the catalog), not silently swapped
        // for the default. Only ids that cannot be a text model fall back to the default.
        if (GeminiTextModelFilter.IsSuitableForTextJsonMapping(trimmed))
        {
            return trimmed;
        }

        _logger.Warning(
            $"Gemini model '{trimmed}' is not a text model; using '{DefaultModel}' instead.",
            LogTarget.All);
        return DefaultModel;
    }

    public void ReloadFromDisk()
    {
        try
        {
            Directory.CreateDirectory(_settingsService.Current.StateFolder);
            if (!File.Exists(CatalogFilePath))
            {
                _current = GeminiModelCatalogDefaults.CreateSeed();
                SaveToDisk();
                return;
            }

            var json = File.ReadAllText(CatalogFilePath);
            var loaded = JsonSerializer.Deserialize<GeminiModelCatalogFile>(json, FileJsonOptions);
            if (loaded is null || loaded.Models.Count == 0)
            {
                _current = GeminiModelCatalogDefaults.CreateSeed();
                SaveToDisk();
                return;
            }

            _current = loaded;
            if (loaded.SeedVersion < GeminiModelCatalogDefaults.SeedVersion)
            {
                MergeSeed(_current, GeminiModelCatalogDefaults.CreateSeed());
                SaveToDisk();
                _logger.Info(
                    $"Gemini model catalog updated to built-in list v{GeminiModelCatalogDefaults.SeedVersion}.",
                    LogTarget.All);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to load Gemini model catalog from '{CatalogFilePath}': {ex.Message}", LogTarget.All);
            _current = GeminiModelCatalogDefaults.CreateSeed();
        }
    }

    public async Task<GeminiModelRefreshResult> RefreshFromApiAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = _settingsService.Current.Gemini?.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        var availableModels = await ListAvailableModelIdsAsync(apiKey, cancellationToken);
        if (availableModels.Count == 0)
        {
            throw new InvalidOperationException(
                "Gemini ListModels returned no generateContent models. Catalog was not changed.");
        }

        var newlyMarked = 0;
        var newlyAdded = 0;
        var restored = 0;

        foreach (var apiId in availableModels
                     .Where(id => id.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
                     .Where(GeminiTextModelFilter.IsSuitableForTextJsonMapping)
                     .OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            if (FindEntry(apiId) is not null)
            {
                continue;
            }

            _current.Models.Add(new GeminiModelEntry
            {
                Id = apiId,
                DisplayName = FormatDisplayName(apiId),
                Deprecated = false,
                SupportsTextMapping = false,
                Notes = "Added from Gemini ListModels API; set supportsTextMapping to true to use as fallback"
            });
            newlyAdded++;
        }

        foreach (var entry in _current.Models)
        {
            if (availableModels.Contains(entry.Id))
            {
                if (entry.Deprecated && string.IsNullOrWhiteSpace(entry.Replacement))
                {
                    entry.Deprecated = false;
                    restored++;
                }

                continue;
            }

            if (!entry.Deprecated)
            {
                newlyMarked++;
            }

            entry.Deprecated = true;
        }

        _current.LastRefreshedUtc = DateTime.UtcNow;
        SaveToDisk();

        var activeCount = _current.Models.Count(entry => !entry.Deprecated);
        var deprecatedCount = _current.Models.Count - activeCount;
        var summary =
            $"{activeCount} active, {deprecatedCount} deprecated, {newlyMarked} newly marked unavailable, {newlyAdded} added, {restored} restored ({availableModels.Count} API models).";

        _logger.Info($"Gemini model catalog refresh: {summary}", LogTarget.All);

        return new GeminiModelRefreshResult
        {
            ActiveCount = activeCount,
            DeprecatedCount = deprecatedCount,
            NewlyMarkedDeprecatedCount = newlyMarked,
            NewlyAddedCount = newlyAdded,
            RestoredCount = restored,
            ApiModelCount = availableModels.Count,
            Summary = summary
        };
    }

    /// <summary>
    /// Updates the models the built-in seed knows (flags, limits, dates, thinking, rank) and adds new seed models.
    /// Entries the seed does not know (added by ListModels or by hand) are kept as they are.
    /// </summary>
    public static void MergeSeed(GeminiModelCatalogFile target, GeminiModelCatalogFile seed)
    {
        foreach (var seedEntry in seed.Models)
        {
            var existing = target.Models.FirstOrDefault(entry =>
                string.Equals(entry.Id, seedEntry.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                target.Models.Add(seedEntry);
                continue;
            }

            existing.DisplayName = seedEntry.DisplayName;
            existing.Deprecated = seedEntry.Deprecated;
            existing.Replacement = seedEntry.Replacement;
            existing.Notes = seedEntry.Notes;
            existing.SupportsTextMapping = seedEntry.SupportsTextMapping;
            existing.AutoRoute = seedEntry.AutoRoute;
            existing.Rank = seedEntry.Rank;
            existing.FreeRequestsPerMinute = seedEntry.FreeRequestsPerMinute;
            existing.FreeRequestsPerDay = seedEntry.FreeRequestsPerDay;
            existing.ThinkingLevel = seedEntry.ThinkingLevel;
            existing.ThinkingBudget = seedEntry.ThinkingBudget;
            existing.RetireAfterUtc = seedEntry.RetireAfterUtc;
        }

        bool IsUsable(string id) => target.Models.Any(entry =>
            string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase) && !entry.Deprecated);

        if (string.IsNullOrWhiteSpace(target.DefaultModel) || !IsUsable(target.DefaultModel))
        {
            target.DefaultModel = seed.DefaultModel;
        }

        if (target.FallbackModels.Count == 0 || target.FallbackModels.Any(id => !IsUsable(id)))
        {
            target.FallbackModels = [.. seed.FallbackModels];
        }

        target.SeedVersion = seed.SeedVersion;
    }

    private async Task<HashSet<string>> ListAvailableModelIdsAsync(string apiKey, CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? pageToken = null;
        var pages = 0;
        do
        {
            var url = $"{AppConstants.GeminiApiBaseUrl}models?pageSize=1000";
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-goog-api-key", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Gemini ListModels returned HTTP {(int)response.StatusCode}: {TrimForLog(responseText)}");
            }

            var parsed = JsonSerializer.Deserialize<GeminiListModelsResponse>(responseText, ApiJsonOptions);
            foreach (var model in parsed?.Models ?? [])
            {
                if (string.IsNullOrWhiteSpace(model.Name))
                {
                    continue;
                }

                var id = model.Name.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                    ? model.Name["models/".Length..]
                    : model.Name;

                if (model.SupportedGenerationMethods is null ||
                    model.SupportedGenerationMethods.Any(method =>
                        string.Equals(method, "generateContent", StringComparison.OrdinalIgnoreCase)))
                {
                    ids.Add(id);
                }
            }

            pageToken = parsed?.NextPageToken;
            pages++;
        }
        while (!string.IsNullOrWhiteSpace(pageToken) && pages < 20);

        _logger.Debug(
            $"Gemini ListModels parsed {ids.Count} generateContent model(s).",
            LogTarget.All);

        return ids;
    }

    private GeminiModelEntry? FindEntry(string modelId) =>
        _current.Models.FirstOrDefault(entry =>
            string.Equals(entry.Id, modelId, StringComparison.OrdinalIgnoreCase));

    private void SaveToDisk()
    {
        Directory.CreateDirectory(_settingsService.Current.StateFolder);
        var json = JsonSerializer.Serialize(_current, FileJsonOptions);
        File.WriteAllText(CatalogFilePath, json);
    }

    private static string FormatDisplayName(string modelId)
    {
        var label = modelId;
        if (label.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
        {
            label = label["gemini-".Length..];
        }

        return string.Join(' ', label
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string TrimForLog(string value) =>
        value.Length <= 240 ? value : value[..240] + "...";

    private sealed class GeminiListModelsResponse
    {
        public List<GeminiListedModel>? Models { get; set; }

        public string? NextPageToken { get; set; }
    }

    private sealed class GeminiListedModel
    {
        public string? Name { get; set; }

        public List<string>? SupportedGenerationMethods { get; set; }
    }
}
