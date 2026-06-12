using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class RecipeWorkspaceViewModel : ViewModelBase
{
    private readonly IRecipeService _recipeService;

    public RecipeWorkspaceViewModel(IRecipeService recipeService)
    {
        _recipeService = recipeService;
        ReloadRecipes();
    }

    public ObservableCollection<RecipeListItemViewModel> Recipes { get; } = [];

    public ObservableCollection<RecipeModuleEditorViewModel> Modules { get; } = [];

    public IReadOnlyList<MediaKind> TargetKinds { get; } = [MediaKind.TvEpisode, MediaKind.TvSeasonPack, MediaKind.Movie];

    [ObservableProperty]
    private RecipeListItemViewModel? selectedRecipeItem;

    [ObservableProperty]
    private RecipeModuleEditorViewModel? selectedModule;

    [ObservableProperty]
    private string statusMessage = "Select a recipe module to edit its search behavior.";

    public SearchRecipe? SelectedRecipe => SelectedRecipeItem?.Recipe;

    public bool HasRecipes => Recipes.Count > 0;

    public bool HasSelectedRecipe => SelectedRecipe is not null;

    public bool HasSelectedModule => SelectedModule is not null;

    public string SelectedRecipeName
    {
        get => SelectedRecipe?.Name ?? string.Empty;
        set
        {
            if (SelectedRecipe is null || SelectedRecipe.Name == value)
            {
                return;
            }

            SelectedRecipe.Name = value;
            SelectedRecipeItem!.Refresh();
            OnPropertyChanged();
        }
    }

    public MediaKind SelectedRecipeTargetKind
    {
        get => SelectedRecipe?.TargetKind ?? MediaKind.TvEpisode;
        set
        {
            if (SelectedRecipe is null || SelectedRecipe.TargetKind == value)
            {
                return;
            }

            SelectedRecipe.TargetKind = value;
            SelectedRecipeItem!.Refresh();
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void SelectRecipe(RecipeListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedRecipeItem = item;
    }

    [RelayCommand]
    private void SelectModule(RecipeModuleEditorViewModel? module)
    {
        if (module is null)
        {
            return;
        }

        SelectedModule = module;
    }

    [RelayCommand]
    private void RefreshRecipesFolder()
    {
        var selectedId = SelectedRecipe?.RecipeId;
        _recipeService.ReloadFromDisk();
        ReloadRecipes(selectedId);
        StatusMessage = $"Refreshed recipe folder. Found {Recipes.Count} recipe(s).";
    }

    [RelayCommand]
    private void CreateRecipe()
    {
        var defaultRecipe = _recipeService.GetDefaultRecipe(MediaKind.TvEpisode);
        var recipe = _recipeService.SaveRecipe(new SearchRecipe
        {
            Name = $"Recipe {Recipes.Count + 1}",
            TargetKind = MediaKind.TvEpisode,
            Modules = defaultRecipe.Modules
                .Select(CloneModule)
                .ToList()
        });
        ReloadRecipes(recipe.RecipeId);
        StatusMessage = $"Created recipe '{recipe.Name}'.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRecipe))]
    private void SaveRecipe()
    {
        if (SelectedRecipe is null)
        {
            return;
        }

        _recipeService.SaveRecipe(SelectedRecipe);
        SelectedRecipeItem?.Refresh();
        StatusMessage = $"Saved recipe '{SelectedRecipeName}'.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRecipe))]
    private void DuplicateRecipe()
    {
        if (SelectedRecipe is null)
        {
            return;
        }

        var copy = _recipeService.DuplicateRecipe(SelectedRecipe.RecipeId);
        ReloadRecipes(copy.RecipeId);
        StatusMessage = $"Duplicated recipe as '{copy.Name}'.";
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedRecipe))]
    private void DeleteRecipe()
    {
        if (SelectedRecipe is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Delete recipe '{SelectedRecipe.Name}'?",
            "Delete Recipe",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var deletedName = SelectedRecipe.Name;
        _recipeService.DeleteRecipe(SelectedRecipe.RecipeId);
        ReloadRecipes();
        StatusMessage = $"Deleted recipe '{deletedName}'.";
    }

    partial void OnSelectedRecipeItemChanged(RecipeListItemViewModel? value)
    {
        foreach (var recipe in Recipes)
        {
            recipe.IsSelected = ReferenceEquals(recipe, value);
        }

        LoadModules(value?.Recipe);
        OnPropertyChanged(nameof(SelectedRecipe));
        OnPropertyChanged(nameof(SelectedRecipeName));
        OnPropertyChanged(nameof(SelectedRecipeTargetKind));
        NotifySelectionStateChanged();
    }

    partial void OnSelectedModuleChanged(RecipeModuleEditorViewModel? value)
    {
        foreach (var module in Modules)
        {
            module.IsSelected = ReferenceEquals(module, value);
        }

        OnPropertyChanged(nameof(HasSelectedModule));
    }

    private void ReloadRecipes(string? selectedRecipeId = null)
    {
        selectedRecipeId ??= SelectedRecipe?.RecipeId;
        Recipes.Clear();
        foreach (var recipe in _recipeService.GetRecipes())
        {
            Recipes.Add(new RecipeListItemViewModel(recipe));
        }

        SelectedRecipeItem = Recipes.FirstOrDefault(item => item.Recipe.RecipeId == selectedRecipeId)
            ?? Recipes.FirstOrDefault();
        OnPropertyChanged(nameof(HasRecipes));
        NotifySelectionStateChanged();
    }

    private void LoadModules(SearchRecipe? recipe)
    {
        var selectedBlockType = SelectedModule?.BlockType;
        Modules.Clear();
        if (recipe is null)
        {
            SelectedModule = null;
            return;
        }

        foreach (var module in recipe.Modules.OrderBy(module => module.Order))
        {
            Modules.Add(new RecipeModuleEditorViewModel(module));
        }

        SelectedModule = selectedBlockType is not null
            ? Modules.FirstOrDefault(module => module.BlockType == selectedBlockType) ?? Modules.FirstOrDefault()
            : Modules.FirstOrDefault();
    }

    private bool CanDeleteSelectedRecipe()
    {
        return SelectedRecipe is not null &&
               !SelectedRecipe.RecipeId.StartsWith("default-", StringComparison.OrdinalIgnoreCase);
    }

    private void NotifySelectionStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedRecipe));
        OnPropertyChanged(nameof(HasSelectedModule));
        SaveRecipeCommand.NotifyCanExecuteChanged();
        DuplicateRecipeCommand.NotifyCanExecuteChanged();
        DeleteRecipeCommand.NotifyCanExecuteChanged();
    }

    private static RecipeModuleConfig CloneModule(RecipeModuleConfig module)
    {
        return new RecipeModuleConfig
        {
            ModuleId = Guid.NewGuid().ToString("N"),
            BlockType = module.BlockType,
            Order = module.Order,
            SchemaVersion = module.SchemaVersion,
            IsEnabled = module.IsEnabled,
            DisplayName = module.DisplayName,
            Aliases = module.Aliases.ToList(),
            QueryTemplates = module.QueryTemplates.ToList(),
            QualityAllowList = module.QualityAllowList.ToList(),
            PreferredAudioCodec = module.PreferredAudioCodec,
            MinimumSeeders = module.MinimumSeeders,
            MaximumSizeBytes = module.MaximumSizeBytes,
            IncludeTerms = module.IncludeTerms.ToList(),
            ExcludeTerms = module.ExcludeTerms.ToList(),
            PreferredReleaseGroups = module.PreferredReleaseGroups.ToList(),
            BlockedReleaseGroups = module.BlockedReleaseGroups.ToList(),
            Plugins = module.Plugins,
            Category = module.Category,
            ResultLimit = module.ResultLimit,
            SavePath = module.SavePath,
            TorrentCategory = module.TorrentCategory,
            Tags = module.Tags,
            Paused = module.Paused,
            ExtensionData = module.ExtensionData.ToDictionary()
        };
    }
}

public sealed partial class RecipeListItemViewModel : ObservableObject
{
    public RecipeListItemViewModel(SearchRecipe recipe)
    {
        Recipe = recipe;
    }

    public SearchRecipe Recipe { get; }

    public string Name => Recipe.Name;

    public string TargetLabel => Recipe.TargetKind switch
    {
        MediaKind.Movie => "Movie",
        MediaKind.TvSeasonPack => "TV Pack",
        _ => "TV Episode"
    };

    public string ModuleCountLabel => $"{Recipe.Modules.Count} module(s)";

    [ObservableProperty]
    private bool isSelected;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TargetLabel));
        OnPropertyChanged(nameof(ModuleCountLabel));
    }
}

public sealed partial class RecipeModuleEditorViewModel : ObservableObject
{
    private const string CustomQueryKey = "customQuery";
    private const string ParallelSearchMode = "Parallel episode search";
    private const string SnapshotSearchMode = "Show snapshot search";
    private static readonly string[] StandardQualities = ["2160p", "1080p", "720p", "480p"];

    private readonly RecipeModuleConfig _module;

    public RecipeModuleEditorViewModel(RecipeModuleConfig module)
    {
        _module = module;
        QualityOptions = new ObservableCollection<QualityOptionViewModel>(
            StandardQualities.Select(label => new QualityOptionViewModel(
                label,
                _module.QualityAllowList.Any(quality => string.Equals(quality, label, StringComparison.OrdinalIgnoreCase)),
                SyncQualitiesFromOptions)));
    }

    public ObservableCollection<QualityOptionViewModel> QualityOptions { get; }

    public IReadOnlyList<string> SearchModeOptions { get; } = [ParallelSearchMode, SnapshotSearchMode];

    public IReadOnlyList<string> EpisodeNumberingModeOptions { get; } =
    [
        RecipeRuntimeSettings.StandardTvEpisodeNumbering,
        RecipeRuntimeSettings.AnimeAbsoluteEpisodeNumbering
    ];

    public RecipeBlockType BlockType => _module.BlockType;

    public string BlockTypeLabel => _module.BlockType switch
    {
        RecipeBlockType.Identity => "Identity",
        RecipeBlockType.QueryBuilder => "Query",
        RecipeBlockType.SearchSource => "Search",
        RecipeBlockType.CandidateParser => "Parser",
        RecipeBlockType.CandidateFilter => "Quality",
        RecipeBlockType.Scoring => "Scoring",
        _ => _module.BlockType.ToString()
    };

    public string IconKind => _module.BlockType switch
    {
        RecipeBlockType.Identity => "Tags",
        RecipeBlockType.QueryBuilder => "Search",
        RecipeBlockType.SearchSource => "Globe",
        RecipeBlockType.CandidateParser => "Search",
        RecipeBlockType.CandidateFilter => "Tags",
        RecipeBlockType.Scoring => "Tags",
        _ => "ScrollText"
    };

    public string ModuleHint => _module.BlockType switch
    {
        RecipeBlockType.Identity => "Aliases and title matching for the media item.",
        RecipeBlockType.QueryBuilder => "Query templates, custom query, preferred quality, and audio tokens.",
        RecipeBlockType.SearchSource => "Choose parallel per-episode search or show-level snapshot matching.",
        RecipeBlockType.CandidateParser => "Candidate filename parsing is currently automatic.",
        RecipeBlockType.CandidateFilter => "Quality, seeders, size, include/exclude terms, and release groups.",
        RecipeBlockType.Scoring => "Ranking weights are currently fixed by the candidate scoring service.",
        _ => "Recipe module settings."
    };

    public IReadOnlyList<ModuleFieldHelpItem> HelpItems => ModuleFieldHelp.GetItems(_module.BlockType);

    public IReadOnlyList<string> ModuleSummaryLines => BuildSummary();

    public string SelectedQualitiesSummary
    {
        get
        {
            var selected = QualityOptions.Where(option => option.IsSelected).Select(option => option.Label).ToList();
            return selected.Count == 0 ? "(none selected)" : string.Join(", ", selected);
        }
    }

    public bool IsEnabled
    {
        get => _module.IsEnabled;
        set => SetModuleValue(_module.IsEnabled, value, next => _module.IsEnabled = next);
    }

    public string DisplayName
    {
        get => _module.DisplayName;
        set => SetModuleValue(_module.DisplayName, value, next => _module.DisplayName = next);
    }

    public string AliasesText
    {
        get => ToLines(_module.Aliases);
        set => SetListValue(_module.Aliases, SplitTerms(value));
    }

    public string QueryTemplatesText
    {
        get => ToLines(_module.QueryTemplates);
        set => SetListValue(_module.QueryTemplates, SplitLines(value));
    }

    public string CustomQuery
    {
        get => GetExtensionValue(CustomQueryKey);
        set => SetExtensionValue(CustomQueryKey, value);
    }

    public string QualityAllowListText
    {
        get => ToLines(_module.QualityAllowList);
        set => SetListValue(_module.QualityAllowList, SplitTerms(value));
    }

    public string PreferredAudioCodec
    {
        get => _module.PreferredAudioCodec;
        set => SetModuleValue(_module.PreferredAudioCodec, value, next => _module.PreferredAudioCodec = next);
    }

    public int MinimumSeeders
    {
        get => _module.MinimumSeeders;
        set => SetModuleValue(_module.MinimumSeeders, Math.Max(0, value), next => _module.MinimumSeeders = next);
    }

    public long? MaximumSizeGb
    {
        get => _module.MaximumSizeBytes is null ? null : _module.MaximumSizeBytes.Value / 1024 / 1024 / 1024;
        set
        {
            long? sizeBytes = value is null or <= 0 ? null : value.Value * 1024 * 1024 * 1024;
            SetModuleValue(_module.MaximumSizeBytes, sizeBytes, next => _module.MaximumSizeBytes = next);
        }
    }

    /// <summary>0 means no size limit (for NumericUpDown binding).</summary>
    public int MaximumSizeGbValue
    {
        get => MaximumSizeGb is null or <= 0 ? 0 : (int)Math.Min(MaximumSizeGb.Value, 500);
        set => MaximumSizeGb = value <= 0 ? null : value;
    }

    public string IncludeTermsText
    {
        get => ToLines(_module.IncludeTerms);
        set => SetListValue(_module.IncludeTerms, SplitTerms(value));
    }

    public string ExcludeTermsText
    {
        get => ToLines(_module.ExcludeTerms);
        set => SetListValue(_module.ExcludeTerms, SplitTerms(value));
    }

    public string PreferredReleaseGroupsText
    {
        get => ToLines(_module.PreferredReleaseGroups);
        set => SetListValue(_module.PreferredReleaseGroups, SplitTerms(value));
    }

    public string BlockedReleaseGroupsText
    {
        get => ToLines(_module.BlockedReleaseGroups);
        set => SetListValue(_module.BlockedReleaseGroups, SplitTerms(value));
    }

    public string Plugins
    {
        get => _module.Plugins;
        set => SetModuleValue(_module.Plugins, value, next => _module.Plugins = next);
    }

    public string Category
    {
        get => _module.Category;
        set => SetModuleValue(_module.Category, value, next => _module.Category = next);
    }

    public int ResultLimit
    {
        get => _module.ResultLimit;
        set => SetModuleValue(_module.ResultLimit, Math.Clamp(value, 1, 5000), next => _module.ResultLimit = next);
    }

    public int ParallelSearchCount
    {
        get => GetExtensionInt(RecipeRuntimeSettings.ParallelSearchCountKey, 3, 1, 8);
        set => SetExtensionValue(RecipeRuntimeSettings.ParallelSearchCountKey, Math.Clamp(value, 1, 8).ToString());
    }

    public int MaxCandidatesPerFetch
    {
        get => GetExtensionInt(RecipeRuntimeSettings.MaxCandidatesPerFetchKey, 3, 1, 10);
        set => SetExtensionValue(RecipeRuntimeSettings.MaxCandidatesPerFetchKey, Math.Clamp(value, 1, 10).ToString());
    }

    public bool DeduplicateCandidates
    {
        get => GetExtensionBool(RecipeRuntimeSettings.DeduplicateCandidatesKey, true);
        set
        {
            SetExtensionValue(RecipeRuntimeSettings.DeduplicateCandidatesKey, value.ToString());
            OnPropertyChanged(nameof(IsFuzzyDeduplicateEnabled));
        }
    }

    public bool FuzzyDeduplicate
    {
        get => GetExtensionBool(RecipeRuntimeSettings.FuzzyDeduplicateKey, false);
        set
        {
            SetExtensionValue(RecipeRuntimeSettings.FuzzyDeduplicateKey, value.ToString());
            OnPropertyChanged(nameof(IsFuzzyDeduplicateEnabled));
        }
    }

    public int FuzzyDeduplicateSizeToleranceMb
    {
        get => GetExtensionInt(RecipeRuntimeSettings.FuzzyDeduplicateSizeToleranceMbKey, 5, 0, 100);
        set => SetExtensionValue(
            RecipeRuntimeSettings.FuzzyDeduplicateSizeToleranceMbKey,
            Math.Clamp(value, 0, 100).ToString());
    }

    public bool IsFuzzyDeduplicateEnabled => DeduplicateCandidates && FuzzyDeduplicate;

    public bool UseShowSnapshotSearch
    {
        get => GetExtensionBool(RecipeRuntimeSettings.UseShowSnapshotSearchKey, false);
        set
        {
            SetExtensionValue(RecipeRuntimeSettings.UseShowSnapshotSearchKey, value.ToString());
            OnPropertyChanged(nameof(SearchMode));
            OnPropertyChanged(nameof(IsParallelSearchMode));
            OnPropertyChanged(nameof(IsSnapshotSearchMode));
        }
    }

    public string SearchMode
    {
        get => UseShowSnapshotSearch ? SnapshotSearchMode : ParallelSearchMode;
        set => UseShowSnapshotSearch = string.Equals(value, SnapshotSearchMode, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsParallelSearchMode => !UseShowSnapshotSearch;

    public bool IsSnapshotSearchMode => UseShowSnapshotSearch;

    public int SnapshotTargetResults
    {
        get => GetExtensionInt(RecipeRuntimeSettings.SnapshotTargetResultsKey, 2000, 100, 5000);
        set => SetExtensionValue(RecipeRuntimeSettings.SnapshotTargetResultsKey, Math.Clamp(value, 100, 5000).ToString());
    }

    public int SnapshotTimeoutSeconds
    {
        get => GetExtensionInt(RecipeRuntimeSettings.SnapshotTimeoutSecondsKey, 120, 30, 300);
        set => SetExtensionValue(RecipeRuntimeSettings.SnapshotTimeoutSecondsKey, Math.Clamp(value, 30, 300).ToString());
    }

    public int SnapshotIdleTimeoutSeconds
    {
        get => GetExtensionInt(RecipeRuntimeSettings.SnapshotIdleTimeoutSecondsKey, 10, 0, 120);
        set => SetExtensionValue(RecipeRuntimeSettings.SnapshotIdleTimeoutSecondsKey, Math.Clamp(value, 0, 120).ToString());
    }

    public int LocalMatchWorkers
    {
        get => GetExtensionInt(RecipeRuntimeSettings.LocalMatchWorkersKey, 3, 1, 8);
        set => SetExtensionValue(RecipeRuntimeSettings.LocalMatchWorkersKey, Math.Clamp(value, 1, 8).ToString());
    }

    public bool EnableCandidateMetadataProbe
    {
        get => GetExtensionBool(RecipeRuntimeSettings.EnableCandidateMetadataProbeKey, false);
        set => SetExtensionValue(RecipeRuntimeSettings.EnableCandidateMetadataProbeKey, value.ToString());
    }

    public string EpisodeNumberingMode
    {
        get => RecipeRuntimeSettings.NormalizeEpisodeNumberingMode(
            GetExtensionValue(RecipeRuntimeSettings.EpisodeNumberingModeKey));
        set => SetExtensionValue(
            RecipeRuntimeSettings.EpisodeNumberingModeKey,
            RecipeRuntimeSettings.NormalizeEpisodeNumberingMode(value));
    }

    public string SavePath
    {
        get => _module.SavePath;
        set => SetModuleValue(_module.SavePath, value, next => _module.SavePath = next);
    }

    public string TorrentCategory
    {
        get => _module.TorrentCategory;
        set => SetModuleValue(_module.TorrentCategory, value, next => _module.TorrentCategory = next);
    }

    public string Tags
    {
        get => _module.Tags;
        set => SetModuleValue(_module.Tags, value, next => _module.Tags = next);
    }

    public bool Paused
    {
        get => _module.Paused;
        set => SetModuleValue(_module.Paused, value, next => _module.Paused = next);
    }

    [ObservableProperty]
    private bool isSelected;

    private void SetModuleValue<T>(T currentValue, T newValue, Action<T> apply)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
        {
            return;
        }

        apply(newValue);
        NotifyStateChanged();
    }

    private void SetListValue(List<string> target, IReadOnlyList<string> values)
    {
        target.Clear();
        target.AddRange(values);
        NotifyStateChanged();
    }

    private void SyncQualitiesFromOptions()
    {
        SetListValue(
            _module.QualityAllowList,
            QualityOptions.Where(option => option.IsSelected).Select(option => option.Label).ToList());
        OnPropertyChanged(nameof(SelectedQualitiesSummary));
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(string.Empty);
        OnPropertyChanged(nameof(ModuleSummaryLines));
        OnPropertyChanged(nameof(SelectedQualitiesSummary));
    }

    private IReadOnlyList<string> BuildSummary() => _module.BlockType switch
    {
        RecipeBlockType.Identity =>
        [
            $"Enabled: {(IsEnabled ? "Yes" : "No")}",
            $"Aliases: {CountLines(AliasesText)}"
        ],
        RecipeBlockType.QueryBuilder =>
        [
            $"Enabled: {(IsEnabled ? "Yes" : "No")}",
            $"Qualities: {SelectedQualitiesSummary}",
            $"Preferred audio: {DisplayOrEmpty(PreferredAudioCodec)}",
            $"Query templates: {CountLines(QueryTemplatesText)}",
            $"Custom query: {DisplayOrEmpty(CustomQuery)}"
        ],
        RecipeBlockType.SearchSource =>
        [
            $"Enabled: {(IsEnabled ? "Yes" : "No")}",
            $"Mode: {SearchMode}",
            $"Plugins: {DisplayOrEmpty(Plugins)}",
            $"Category: {DisplayOrEmpty(Category)}",
            $"Result limit: {ResultLimit}",
            $"Parallel searches: {ParallelSearchCount}",
            $"Candidates per fetch: {MaxCandidatesPerFetch}",
            $"Deduplicate candidates: {(DeduplicateCandidates ? "On" : "Off")}",
            $"Fuzzy dedup: {(FuzzyDeduplicate ? "On" : "Off")}{(FuzzyDeduplicate ? $", size tolerance: {FuzzyDeduplicateSizeToleranceMb} MB" : string.Empty)}",
            $"Snapshot search: {(UseShowSnapshotSearch ? "On" : "Off")}",
            $"Snapshot target: {SnapshotTargetResults}",
            $"Snapshot timeout: {SnapshotTimeoutSeconds}s",
            $"Snapshot idle timeout: {SnapshotIdleTimeoutSeconds}s{(SnapshotIdleTimeoutSeconds == 0 ? " (off)" : string.Empty)}",
            $"Local match workers: {LocalMatchWorkers}"
        ],
        RecipeBlockType.CandidateFilter =>
        [
            $"Enabled: {(IsEnabled ? "Yes" : "No")}",
            $"Qualities: {SelectedQualitiesSummary}",
            $"Minimum seeders: {MinimumSeeders}",
            $"Max size GB: {(MaximumSizeGb?.ToString() ?? "none")}",
            $"Preferred audio: {DisplayOrEmpty(PreferredAudioCodec)}",
            $"Include terms: {CountLines(IncludeTermsText)}",
            $"Exclude terms: {CountLines(ExcludeTermsText)}",
            $"Preferred groups: {CountLines(PreferredReleaseGroupsText)}",
            $"Blocked groups: {CountLines(BlockedReleaseGroupsText)}"
        ],
        RecipeBlockType.CandidateParser =>
        [
            $"Enabled: {(IsEnabled ? "Yes" : "No")}",
            $"Episode numbering: {EpisodeNumberingMode}",
            $"Probe metadata: {(EnableCandidateMetadataProbe ? "On" : "Off")}"
        ],
        RecipeBlockType.Scoring =>
        [
            $"Enabled: {(IsEnabled ? "Yes" : "No")}",
            "Scoring uses quality, audio, seeders, and title match weights."
        ],
        _ => [$"Enabled: {(IsEnabled ? "Yes" : "No")}"]
    };

    private static string DisplayOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();

    private static string CountLines(string value) =>
        string.IsNullOrWhiteSpace(value) ? "0" : SplitLines(value).Count.ToString();

    private string GetExtensionValue(string key)
    {
        return _module.ExtensionData.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private int GetExtensionInt(string key, int defaultValue, int min, int max)
    {
        var value = GetExtensionValue(key);
        return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : defaultValue;
    }

    private bool GetExtensionBool(string key, bool defaultValue)
    {
        var value = GetExtensionValue(key);
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private void SetExtensionValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (_module.ExtensionData.Remove(key))
            {
                NotifyStateChanged();
            }

            return;
        }

        _module.ExtensionData[key] = value.Trim();
        NotifyStateChanged();
    }

    private static string ToLines(IEnumerable<string> values)
    {
        return string.Join(Environment.NewLine, values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static IReadOnlyList<string> SplitLines(string value)
    {
        return value
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> SplitTerms(string value)
    {
        return value
            .Split(["\r\n", "\n", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class ModuleFieldHelpItem
{
    public string FieldName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string OutputImpact { get; init; } = string.Empty;
}

internal static class ModuleFieldHelp
{
    public static IReadOnlyList<ModuleFieldHelpItem> GetItems(RecipeBlockType blockType) =>
        blockType switch
        {
            RecipeBlockType.Identity =>
            [
                new() { FieldName = "Enabled", Description = "Turns identity matching on or off for this recipe.", OutputImpact = "When disabled, only the primary library title is used. Aliases are ignored during search and candidate filtering." },
                new() { FieldName = "Title aliases", Description = "Alternative names for the show or movie.", OutputImpact = "Each alias is used as an extra {title} variant in queries and helps accept torrents that use abbreviations or alternate spellings." }
            ],
            RecipeBlockType.QueryBuilder =>
            [
                new() { FieldName = "Quality allow list", Description = "Accepted quality labels such as 1080p or 2160p.", OutputImpact = "Generates one search query per quality token. More qualities mean more queries and broader search coverage." },
                new() { FieldName = "Preferred audio", Description = "Audio codec or label to prefer, e.g. DDP5.1 or Atmos.", OutputImpact = "Inserted into query templates as {audio}. Candidates containing this token receive a higher score." },
                new() { FieldName = "Query templates", Description = "Patterns sent to qBittorrent search.", OutputImpact = "Each template is expanded with title, year, season, episode, quality, and audio. More templates increase candidate discovery at the cost of more searches." },
                new() { FieldName = "Custom query", Description = "One extra template appended after the generated list.", OutputImpact = "Useful for manual search phrases that do not fit the standard templates." }
            ],
            RecipeBlockType.SearchSource =>
            [
                new() { FieldName = "Plugins", Description = "Which indexer plugins to query. 'enabled' uses all active plugins.", OutputImpact = "Limits or broadens which indexers contribute candidates to the result set." },
                new() { FieldName = "Category", Description = "qBittorrent search category filter.", OutputImpact = "Narrows results to TV, movies, or all categories depending on plugin support." },
                new() { FieldName = "Result limit", Description = "Maximum rows returned per query.", OutputImpact = "Higher values surface more candidates but increase search time and noise." },
                new() { FieldName = "Parallel searches", Description = "How many queries run at the same time.", OutputImpact = "Faster cart execution when set higher, but may hit qBittorrent search capacity limits." },
                new() { FieldName = "Candidates per fetch", Description = "How many accepted candidates are kept per episode or movie fetch.", OutputImpact = "Lower values reduce noise; higher values keep more backup torrent options." },
                new() { FieldName = "Deduplicate candidates", Description = "Remove duplicate torrent URLs before ranking, keeping the copy with the most seeders.", OutputImpact = "Frees candidate slots for distinct torrents when the same release appears from multiple indexers." },
                new() { FieldName = "Fuzzy deduplicate", Description = "Also group candidates by normalized filename and file size bucket.", OutputImpact = "Removes near-duplicate releases that use different tracker URLs but represent the same torrent, such as the same filename with or without a (TV) suffix." },
                new() { FieldName = "Fuzzy dedup size tolerance", Description = "File size bucket width in MB for fuzzy dedup. 0 means exact byte size only.", OutputImpact = "Larger values treat small size differences across trackers as the same release; smaller values are stricter." },
                new() { FieldName = "Snapshot search", Description = "Use one large show-level search snapshot instead of per-episode queries.", OutputImpact = "Faster for full seasons but needs local matching workers." },
                new() { FieldName = "Snapshot target results", Description = "How many rows to collect in the snapshot search.", OutputImpact = "Larger snapshots improve coverage but take longer to finish." },
                new() { FieldName = "Snapshot timeout", Description = "Maximum seconds to wait for snapshot search completion.", OutputImpact = "Prevents hung searches from blocking the fetch job indefinitely." },
                new() { FieldName = "Snapshot idle timeout", Description = "Stop snapshot polling when no new results arrive for this many seconds. Resets whenever new rows are added. 0 disables early stop.", OutputImpact = "Finishes sooner when indexers stop returning new rows, while still respecting the total snapshot timeout." },
                new() { FieldName = "Local match workers", Description = "Parallel workers that match snapshot rows to episodes.", OutputImpact = "More workers speed up snapshot matching on large seasons." }
            ],
            RecipeBlockType.CandidateFilter =>
            [
                new() { FieldName = "Quality", Description = "Allowed quality labels for accepted candidates.", OutputImpact = "Torrents that do not match any listed quality are rejected before scoring." },
                new() { FieldName = "Minimum seeders", Description = "Lowest seeder count still accepted.", OutputImpact = "Higher values reduce dead or slow torrents but may eliminate rare releases." },
                new() { FieldName = "Max size GB", Description = "Optional upper size limit in gigabytes.", OutputImpact = "Oversized packs or remuxes are rejected when set." },
                new() { FieldName = "Preferred audio", Description = "Audio label used for scoring bonus.", OutputImpact = "Does not reject candidates, but boosts ranking when the filename contains this codec." },
                new() { FieldName = "Include terms", Description = "Terms that must appear in the torrent name.", OutputImpact = "Useful to require WEB-DL, x265, or a specific language tag." },
                new() { FieldName = "Exclude terms", Description = "Terms that reject a candidate immediately.", OutputImpact = "Common use: block cam, telesync, or unwanted codecs." },
                new() { FieldName = "Preferred release groups", Description = "Groups you prefer when ranking.", OutputImpact = "Currently informational for scoring; blocked groups always reject." },
                new() { FieldName = "Blocked release groups", Description = "Groups that are always rejected.", OutputImpact = "Any matching group name in the torrent title removes the candidate." }
            ],
            RecipeBlockType.CandidateParser =>
            [
                new() { FieldName = "Episode numbering", Description = "Controls whether filenames use Standard TV SxxEyy numbering or Anime absolute numbering such as One Piece - 1163.", OutputImpact = "Standard TV keeps existing behavior. Anime absolute accepts absolute episode numbers for shows that publish that way." },
                new() { FieldName = "Probe candidate metadata", Description = "Download torrent metadata to verify episode/year coverage.", OutputImpact = "Improves accuracy for ambiguous filenames but adds extra qBittorrent requests." }
            ],
            RecipeBlockType.Scoring =>
            [
                new() { FieldName = "Scoring weights", Description = "Quality, audio, seeders, title match, and episode title contribute to total score.", OutputImpact = "The highest-scoring accepted candidate is chosen when Run Cart executes." }
            ],
            _ =>
            [
                new() { FieldName = "Module", Description = "Recipe pipeline step.", OutputImpact = "Each enabled module contributes to how searches run, candidates are filtered, and torrents are added." }
            ]
        };
}
