using System.Text.Json;
using System.Text.Json.Serialization;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class RecipeService : IRecipeService
{
    private const string RecipesFolderName = "Recipes";

    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private bool _suppressChangeEvents;

    public RecipeService(ISettingsService settingsService, IAppLogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public event EventHandler? RecipesChanged;

    public void ReloadFromDisk()
    {
        _ = GetRecipes();
        NotifyRecipesChanged();
    }

    public IReadOnlyList<SearchRecipe> GetRecipes()
    {
        EnsureDefaults();
        return Directory.EnumerateFiles(GetRecipesFolder(), "*.rcp")
            .Select(ReadRecipeFile)
            .Where(recipe => recipe is not null)
            .Select(recipe => recipe!)
            .OrderBy(recipe => recipe.Name)
            .ToList();
    }

    public SearchRecipe GetDefaultRecipe(MediaKind targetKind)
    {
        EnsureDefaults();
        var defaultId = GetDefaultRecipeId(targetKind);
        return ReadRecipeFile(GetRecipePath(defaultId)) ?? CreateDefaultRecipe(targetKind);
    }

    public SearchRecipe GetRecipeOrDefault(string? recipeId, MediaKind targetKind)
    {
        if (!string.IsNullOrWhiteSpace(recipeId))
        {
            var recipe = ReadRecipeFile(GetRecipePath(recipeId));
            if (recipe is not null)
            {
                return recipe;
            }
        }

        return GetDefaultRecipe(targetKind);
    }

    public SearchRecipe SaveRecipe(SearchRecipe recipe)
    {
        NormalizeRecipe(recipe);
        Directory.CreateDirectory(GetRecipesFolder());
        var path = GetRecipePath(recipe.RecipeId);
        File.WriteAllText(path, JsonSerializer.Serialize(recipe, _jsonOptions));
        _logger.Info($"Saved recipe '{recipe.Name}' to {path}", LogTarget.All);
        NotifyRecipesChanged();
        return recipe;
    }

    public SearchRecipe DuplicateRecipe(string recipeId)
    {
        var source = GetRecipeOrDefault(recipeId, MediaKind.TvEpisode);
        var copy = Clone(source);
        copy.RecipeId = Guid.NewGuid().ToString("N");
        copy.Name = $"{source.Name} Copy";
        return SaveRecipe(copy);
    }

    public void DeleteRecipe(string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId) || recipeId.StartsWith("default-", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var path = GetRecipePath(recipeId);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.Info($"Deleted recipe file {path}", LogTarget.All);
            NotifyRecipesChanged();
        }
    }

    public SearchRecipe ImportRecipe(string filePath)
    {
        var recipe = ReadRecipeFile(filePath) ?? throw new InvalidOperationException("Recipe file is invalid.");
        if (string.IsNullOrWhiteSpace(recipe.RecipeId) || File.Exists(GetRecipePath(recipe.RecipeId)))
        {
            recipe.RecipeId = Guid.NewGuid().ToString("N");
        }

        return SaveRecipe(recipe);
    }

    public string ExportRecipe(string recipeId, string folderPath)
    {
        var recipe = GetRecipes().FirstOrDefault(item => string.Equals(item.RecipeId, recipeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Recipe was not found.");
        Directory.CreateDirectory(folderPath);
        var safeName = string.Join("_", recipe.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var destination = Path.Combine(folderPath, $"{safeName}.rcp");
        File.Copy(GetRecipePath(recipe.RecipeId), destination, overwrite: true);
        return destination;
    }

    public string GetRecipePath(string recipeId)
    {
        return Path.Combine(GetRecipesFolder(), $"{recipeId}.rcp");
    }

    private void EnsureDefaults()
    {
        Directory.CreateDirectory(GetRecipesFolder());
        _suppressChangeEvents = true;
        try
        {
            foreach (var targetKind in new[] { MediaKind.TvEpisode, MediaKind.TvSeasonPack, MediaKind.Movie })
            {
                var path = GetRecipePath(GetDefaultRecipeId(targetKind));
                if (!File.Exists(path))
                {
                    SaveRecipe(CreateDefaultRecipe(targetKind));
                }
            }
        }
        finally
        {
            _suppressChangeEvents = false;
        }
    }

    private void NotifyRecipesChanged()
    {
        if (!_suppressChangeEvents)
        {
            RecipesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private string GetRecipesFolder()
    {
        return Path.Combine(_settingsService.Current.StateFolder, RecipesFolderName);
    }

    private SearchRecipe? ReadRecipeFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var recipe = JsonSerializer.Deserialize<SearchRecipe>(File.ReadAllText(path), _jsonOptions);
            if (recipe is null)
            {
                return null;
            }

            NormalizeRecipe(recipe);
            return recipe;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not read recipe file '{path}': {ex.Message}", LogTarget.All);
            return null;
        }
    }

    private SearchRecipe CreateDefaultRecipe(MediaKind targetKind)
    {
        var settings = _settingsService.Current.AutoTorrent;
        var qualities = new[] { "1080p" };
        var recipe = new SearchRecipe
        {
            RecipeId = GetDefaultRecipeId(targetKind),
            Name = targetKind switch
            {
                MediaKind.Movie => "Default Movie Recipe",
                MediaKind.TvSeasonPack => "Default TV Pack Recipe",
                _ => "Default TV Episode Recipe"
            },
            TargetKind = targetKind,
            Modules =
            [
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.Identity,
                    Order = 0,
                    DisplayName = "Identity / Aliases"
                },
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.QueryBuilder,
                    Order = 10,
                    DisplayName = "Query / Custom Query",
                    QualityAllowList = qualities.ToList(),
                    QueryTemplates = targetKind switch
                    {
                        MediaKind.Movie =>
                        [
                            "{title} {year} {quality} {audio}",
                            "{title} {quality}",
                            "{title} {year}"
                        ],
                        MediaKind.TvSeasonPack =>
                        [
                            "{title} S{season:00} complete {quality} {audio}",
                            "{title} season {season} {quality}",
                            "{title} S{season:00} pack {quality}",
                            "{title} {year} season {season} {quality}"
                        ],
                        _ =>
                        [
                            "{title} S{season:00}E{episode:00} {quality} {audio}",
                            "{title} {year} S{season:00}E{episode:00} {quality}",
                            "{title} {season}x{episode:00} {quality}",
                            "{title} S{season:00}E{episode:00}"
                        ]
                    }
                },
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.SearchSource,
                    Order = 20,
                    DisplayName = "Search Method",
                    Plugins = "enabled",
                    Category = "all",
                    ResultLimit = Math.Max(1, settings.MaxCandidatesPerFetch * 25),
                    ExtensionData = new Dictionary<string, string>
                    {
                        [RecipeRuntimeSettings.ParallelSearchCountKey] = Math.Clamp(settings.MaxParallelSearches, 1, 8).ToString(),
                        [RecipeRuntimeSettings.MaxCandidatesPerFetchKey] = Math.Clamp(settings.MaxCandidatesPerFetch, 1, 10).ToString(),
                        [RecipeRuntimeSettings.UseShowSnapshotSearchKey] = settings.UseShowSnapshotSearch.ToString(),
                        [RecipeRuntimeSettings.SnapshotTargetResultsKey] = Math.Clamp(settings.SnapshotTargetResults, 100, 5000).ToString(),
                        [RecipeRuntimeSettings.SnapshotTimeoutSecondsKey] = Math.Clamp(settings.SnapshotTimeoutSeconds, 30, 300).ToString(),
                        [RecipeRuntimeSettings.LocalMatchWorkersKey] = Math.Clamp(settings.LocalMatchWorkers, 1, 8).ToString()
                    }
                },
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.CandidateParser,
                    Order = 30,
                    DisplayName = "Candidate Parser",
                    ExtensionData = new Dictionary<string, string>
                    {
                        [RecipeRuntimeSettings.EnableCandidateMetadataProbeKey] = settings.EnableCandidateMetadataProbe.ToString()
                    }
                },
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.CandidateFilter,
                    Order = 40,
                    DisplayName = "Quality / Seeders / Audio",
                    QualityAllowList = qualities.ToList(),
                    PreferredAudioCodec = string.Empty,
                    MinimumSeeders = 1
                },
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.Scoring,
                    Order = 50,
                    DisplayName = "Scoring"
                },
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.AddTorrent,
                    Order = 60,
                    DisplayName = "Add Torrent",
                    SavePath = settings.DownloadFolder ?? string.Empty,
                    TorrentCategory = string.IsNullOrWhiteSpace(settings.CategoryName) ? "AutoTorrent" : settings.CategoryName
                },
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.LinkOutput,
                    Order = 70,
                    DisplayName = "Link Output"
                }
            ]
        };
        return recipe;
    }

    private static string GetDefaultRecipeId(MediaKind targetKind)
    {
        return targetKind switch
        {
            MediaKind.Movie => "default-movie",
            MediaKind.TvSeasonPack => "default-tv-pack",
            _ => "default-tv"
        };
    }

    private static void NormalizeRecipe(SearchRecipe recipe)
    {
        recipe.RecipeId = string.IsNullOrWhiteSpace(recipe.RecipeId) ? Guid.NewGuid().ToString("N") : recipe.RecipeId.Trim();
        recipe.Name = string.IsNullOrWhiteSpace(recipe.Name) ? "Untitled Recipe" : recipe.Name.Trim();
        recipe.Version = Math.Max(1, recipe.Version);
        recipe.MainFlowVersion = string.IsNullOrWhiteSpace(recipe.MainFlowVersion) ? "recipe-flow-v1" : recipe.MainFlowVersion.Trim();
        recipe.Modules ??= [];
        EnsureRequiredModules(recipe);
        foreach (var module in recipe.Modules)
        {
            module.ModuleId = string.IsNullOrWhiteSpace(module.ModuleId) ? Guid.NewGuid().ToString("N") : module.ModuleId.Trim();
            module.SchemaVersion = Math.Max(1, module.SchemaVersion);
            module.DisplayName = string.IsNullOrWhiteSpace(module.DisplayName) ? module.BlockType.ToString() : module.DisplayName.Trim();
            module.Aliases ??= [];
            module.QueryTemplates ??= [];
            module.QualityAllowList ??= [];
            module.IncludeTerms ??= [];
            module.ExcludeTerms ??= [];
            module.PreferredReleaseGroups ??= [];
            module.BlockedReleaseGroups ??= [];
            module.Plugins = string.IsNullOrWhiteSpace(module.Plugins) ? "enabled" : module.Plugins.Trim();
            module.Category = string.IsNullOrWhiteSpace(module.Category) ? "all" : module.Category.Trim();
            module.ResultLimit = Math.Clamp(module.ResultLimit, 1, 5000);
            module.ExtensionData ??= [];
        }

        recipe.Modules = recipe.Modules
            .OrderBy(module => module.Order)
            .ThenBy(module => module.BlockType)
            .ToList();
    }

    private static void EnsureRequiredModules(SearchRecipe recipe)
    {
        foreach (var blockType in Enum.GetValues<RecipeBlockType>())
        {
            if (recipe.Modules.Any(module => module.BlockType == blockType))
            {
                continue;
            }

            recipe.Modules.Add(new RecipeModuleConfig
            {
                BlockType = blockType,
                Order = GetDefaultOrder(blockType),
                DisplayName = blockType.ToString(),
                IsEnabled = blockType is not RecipeBlockType.AddTorrent and not RecipeBlockType.LinkOutput
            });
        }
    }

    private static int GetDefaultOrder(RecipeBlockType blockType)
    {
        return blockType switch
        {
            RecipeBlockType.Identity => 0,
            RecipeBlockType.QueryBuilder => 10,
            RecipeBlockType.SearchSource => 20,
            RecipeBlockType.CandidateParser => 30,
            RecipeBlockType.CandidateFilter => 40,
            RecipeBlockType.Scoring => 50,
            RecipeBlockType.AddTorrent => 60,
            RecipeBlockType.LinkOutput => 70,
            _ => 100
        };
    }

    private static SearchRecipe Clone(SearchRecipe recipe)
    {
        var json = JsonSerializer.Serialize(recipe);
        return JsonSerializer.Deserialize<SearchRecipe>(json) ?? new SearchRecipe();
    }
}
