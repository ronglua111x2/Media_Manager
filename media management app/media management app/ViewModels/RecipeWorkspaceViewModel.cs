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
            _recipeService.PrepareRecipe(SelectedRecipe);
            LoadModules(SelectedRecipe);
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
        StatusMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Saved recipe '{SelectedRecipeName}'.";
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
            if (module.BlockType == RecipeBlockType.PackExtrasPriority)
            {
                continue;
            }

            var moduleEditor = new RecipeModuleEditorViewModel(module, recipe.TargetKind);
            if (!moduleEditor.IsApplicableToTarget)
            {
                continue;
            }

            Modules.Add(moduleEditor);
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
            CustomQueries = module.CustomQueries.ToList(),
            QualityAllowList = module.QualityAllowList.ToList(),
            PreferredAudioCodec = module.PreferredAudioCodec,
            MinimumSeeders = module.MinimumSeeders,
            MinimumSizeBytes = module.MinimumSizeBytes,
            MaximumSizeBytes = module.MaximumSizeBytes,
            IncludeTerms = module.IncludeTerms.ToList(),
            ExcludeTerms = module.ExcludeTerms.ToList(),
            PreferTerms = module.PreferTerms.ToList(),
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
    private const string MovieSearchMode = "Movie search";
    private const string TvParallelSearchMode = "TV parallel search";
    private const string TvSnapshotSearchMode = "TV snapshot search";

    private readonly RecipeModuleConfig _module;
    private readonly MediaKind _recipeTargetKind;

    public RecipeModuleEditorViewModel(RecipeModuleConfig module, MediaKind recipeTargetKind = MediaKind.TvEpisode)
    {
        _module = module;
        _recipeTargetKind = recipeTargetKind;
        QualityOptions = new ObservableCollection<QualityOptionViewModel>(
            TorrentQuality.AllQualities.Select(label => new QualityOptionViewModel(
                label,
                _module.QualityAllowList.Any(quality => string.Equals(quality, label, StringComparison.OrdinalIgnoreCase)),
                SyncQualitiesFromOptions)));
    }

    public ObservableCollection<QualityOptionViewModel> QualityOptions { get; }

    public IReadOnlyList<string> SearchModeOptions =>
        IsMovieTarget
            ? [MovieSearchMode]
            : [TvParallelSearchMode, TvSnapshotSearchMode];

    public IReadOnlyList<string> EpisodeNumberingModeOptions { get; } =
    [
        RecipeRuntimeSettings.StandardTvEpisodeNumbering,
        RecipeRuntimeSettings.AnimeAbsoluteEpisodeNumbering
    ];

    public RecipeBlockType BlockType => _module.BlockType;

    public bool IsMovieTarget => _recipeTargetKind == MediaKind.Movie;

    public bool IsTvTarget => !IsMovieTarget;

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
        RecipeBlockType.QueryBuilder => "Query templates, custom queries, preferred quality, and audio tokens.",
        RecipeBlockType.SearchSource => IsMovieTarget
            ? "Movie search source settings and timeout."
            : "Choose TV parallel per-episode search or TV snapshot matching.",
        RecipeBlockType.CandidateParser => IsMovieTarget
            ? "Not used for Movie recipes."
            : "Candidate filename parsing is currently automatic.",
        RecipeBlockType.CandidateFilter => "Quality, seeders, size, include/exclude terms, and release groups.",
        RecipeBlockType.Scoring => _recipeTargetKind == MediaKind.TvSeasonPack
            ? "Tune ranking weights, size preference, and pack-only OVA, special, and extra bonuses."
            : "Tune ranking weights for quality, audio, size preference, seeders, and title matching.",
        _ => "Recipe module settings."
    };

    public IReadOnlyList<ModuleFieldHelpItem> HelpItems =>
        ModuleFieldHelp.GetItems(_module.BlockType, _recipeTargetKind, UseShowSnapshotSearch);

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

    public bool UseLibraryEnglishTitles
    {
        get => GetExtensionBool(RecipeRuntimeSettings.UseLibraryEnglishTitlesKey, false);
        set
        {
            SetExtensionValue(RecipeRuntimeSettings.UseLibraryEnglishTitlesKey, value.ToString());
            OnPropertyChanged(nameof(IsUseLibraryEnglishTitlesEnabled));
        }
    }

    public bool IsUseLibraryEnglishTitlesEnabled => UseLibraryEnglishTitles;

    public int MaxLibraryAlternativeTitlesForSearch
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.MaxLibraryAlternativeTitlesForSearchKey,
            RecipeRuntimeSettings.DefaultMaxLibraryAlternativeTitlesForSearch,
            RecipeRuntimeSettings.MinMaxLibraryAlternativeTitlesForSearch,
            RecipeRuntimeSettings.MaxMaxLibraryAlternativeTitlesForSearch);
        set => SetExtensionValue(
            RecipeRuntimeSettings.MaxLibraryAlternativeTitlesForSearchKey,
            Math.Clamp(
                value,
                RecipeRuntimeSettings.MinMaxLibraryAlternativeTitlesForSearch,
                RecipeRuntimeSettings.MaxMaxLibraryAlternativeTitlesForSearch).ToString());
    }

    public string QueryTemplatesText
    {
        get => ToLines(_module.QueryTemplates);
        set => SetListValue(_module.QueryTemplates, SplitLines(value));
    }

    public string CustomQueriesText
    {
        get => ToLines(_module.CustomQueries);
        set => SetListValue(_module.CustomQueries, SplitLines(value));
    }

    public bool SkipDefaultTitle
    {
        get => GetExtensionBool(RecipeRuntimeSettings.SkipDefaultTitleKey, false);
        set => SetExtensionValue(RecipeRuntimeSettings.SkipDefaultTitleKey, value.ToString());
    }

    public bool SanitizeQuery
    {
        get => GetExtensionBool(RecipeRuntimeSettings.SanitizeQueryKey, true);
        set => SetExtensionValue(RecipeRuntimeSettings.SanitizeQueryKey, value.ToString());
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

    public long? MinimumSizeGb
    {
        get => _module.MinimumSizeBytes is null ? null : _module.MinimumSizeBytes.Value / 1024 / 1024 / 1024;
        set
        {
            long? sizeBytes = value is null or <= 0 ? null : value.Value * 1024 * 1024 * 1024;
            SetModuleValue(_module.MinimumSizeBytes, sizeBytes, next => _module.MinimumSizeBytes = next);
        }
    }

    /// <summary>0 means no size floor (for NumericUpDown binding).</summary>
    public int MinimumSizeGbValue
    {
        get => MinimumSizeGb is null or <= 0 ? 0 : (int)Math.Min(MinimumSizeGb.Value, 500);
        set => MinimumSizeGb = value <= 0 ? null : value;
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

    public string PreferTermsText
    {
        get => ToLines(_module.PreferTerms);
        set => SetListValue(_module.PreferTerms, SplitTerms(value));
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
            var normalized = IsMovieTarget ? false : value;
            SetExtensionValue(RecipeRuntimeSettings.UseShowSnapshotSearchKey, normalized.ToString());
            OnPropertyChanged(nameof(SearchMode));
            OnPropertyChanged(nameof(SearchModeOptions));
            OnPropertyChanged(nameof(IsMovieSearchMode));
            OnPropertyChanged(nameof(IsParallelSearchMode));
            OnPropertyChanged(nameof(IsSnapshotSearchMode));
            OnPropertyChanged(nameof(ShowNonPaginationMovieControls));
            OnPropertyChanged(nameof(ShowNonPaginationTvParallelControls));
            OnPropertyChanged(nameof(ShowPaginationMovieControls));
            OnPropertyChanged(nameof(ShowPaginationTvParallelControls));
            OnPropertyChanged(nameof(ShowPaginationTvSnapshotControls));
        }
    }

    public string SearchMode
    {
        get => IsMovieTarget
            ? MovieSearchMode
            : UseShowSnapshotSearch
                ? TvSnapshotSearchMode
                : TvParallelSearchMode;
        set
        {
            if (IsMovieTarget)
            {
                UseShowSnapshotSearch = false;
                return;
            }

            UseShowSnapshotSearch = string.Equals(value, TvSnapshotSearchMode, StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsMovieSearchMode => IsMovieTarget;

    public bool IsParallelSearchMode => IsTvTarget && !UseShowSnapshotSearch;

    public bool IsSnapshotSearchMode => IsTvTarget && UseShowSnapshotSearch;

    public bool ShowNonPaginationMovieControls => IsMovieSearchMode && !EnableSearchPagination;

    public bool ShowNonPaginationTvParallelControls => IsParallelSearchMode && !EnableSearchPagination;

    public bool ShowPaginationMovieControls => IsMovieSearchMode && EnableSearchPagination;

    public bool ShowPaginationTvParallelControls => IsParallelSearchMode && EnableSearchPagination;

    public bool ShowPaginationTvSnapshotControls => IsSnapshotSearchMode && EnableSearchPagination;

    public int SnapshotTargetResults
    {
        get => GetExtensionInt(RecipeRuntimeSettings.SnapshotTargetResultsKey, 2000, 100, 5000);
        set => SetExtensionValue(RecipeRuntimeSettings.SnapshotTargetResultsKey, Math.Clamp(value, 100, 5000).ToString());
    }

    public int MovieSearchTimeoutSeconds
    {
        get => GetExtensionInt(RecipeRuntimeSettings.MovieSearchTimeoutSecondsKey, 30, 10, 300);
        set => SetExtensionValue(RecipeRuntimeSettings.MovieSearchTimeoutSecondsKey, Math.Clamp(value, 10, 300).ToString());
    }

    public int ParallelSearchTimeoutSeconds
    {
        get => GetExtensionInt(RecipeRuntimeSettings.ParallelSearchTimeoutSecondsKey, 30, 10, 300);
        set => SetExtensionValue(RecipeRuntimeSettings.ParallelSearchTimeoutSecondsKey, Math.Clamp(value, 10, 300).ToString());
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

    public bool EnableCandidateDebugLog
    {
        get => GetExtensionBool(RecipeRuntimeSettings.EnableCandidateDebugLogKey, false);
        set => SetExtensionValue(RecipeRuntimeSettings.EnableCandidateDebugLogKey, value.ToString());
    }

    public bool EnableSearchPagination
    {
        get => GetExtensionBool(RecipeRuntimeSettings.EnableSearchPaginationKey, IsMovieTarget);
        set
        {
            SetExtensionValue(RecipeRuntimeSettings.EnableSearchPaginationKey, value.ToString());
            OnPropertyChanged(nameof(IsSearchPaginationEnabled));
            OnPropertyChanged(nameof(ShowNonPaginationMovieControls));
            OnPropertyChanged(nameof(ShowNonPaginationTvParallelControls));
            OnPropertyChanged(nameof(ShowPaginationMovieControls));
            OnPropertyChanged(nameof(ShowPaginationTvParallelControls));
            OnPropertyChanged(nameof(ShowPaginationTvSnapshotControls));
        }
    }

    public bool IsSearchPaginationEnabled => EnableSearchPagination;

    public int PaginationPageSize
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.PaginationPageSizeKey,
            RecipeRuntimeSettings.DefaultPaginationPageSize,
            RecipeRuntimeSettings.MinPaginationPageSize,
            RecipeRuntimeSettings.MaxPaginationPageSize);
        set => SetExtensionValue(
            RecipeRuntimeSettings.PaginationPageSizeKey,
            Math.Clamp(
                value,
                RecipeRuntimeSettings.MinPaginationPageSize,
                RecipeRuntimeSettings.MaxPaginationPageSize).ToString());
    }

    public int PaginationMaxPagesMovie
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.PaginationMaxPagesMovieKey,
            RecipeRuntimeSettings.DefaultPaginationMaxPagesMovie,
            RecipeRuntimeSettings.MinPaginationMaxPages,
            RecipeRuntimeSettings.MaxPaginationMaxPages);
        set => SetExtensionValue(
            RecipeRuntimeSettings.PaginationMaxPagesMovieKey,
            Math.Clamp(
                value,
                RecipeRuntimeSettings.MinPaginationMaxPages,
                RecipeRuntimeSettings.MaxPaginationMaxPages).ToString());
    }

    public int PaginationMaxPagesTvParallel
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.PaginationMaxPagesTvParallelKey,
            RecipeRuntimeSettings.DefaultPaginationMaxPagesTvParallel,
            RecipeRuntimeSettings.MinPaginationMaxPages,
            RecipeRuntimeSettings.MaxPaginationMaxPages);
        set => SetExtensionValue(
            RecipeRuntimeSettings.PaginationMaxPagesTvParallelKey,
            Math.Clamp(
                value,
                RecipeRuntimeSettings.MinPaginationMaxPages,
                RecipeRuntimeSettings.MaxPaginationMaxPages).ToString());
    }

    public int PaginationMaxPagesTvSnapshot
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.PaginationMaxPagesTvSnapshotKey,
            RecipeRuntimeSettings.DefaultPaginationMaxPagesTvSnapshot,
            RecipeRuntimeSettings.MinPaginationMaxPages,
            RecipeRuntimeSettings.MaxPaginationMaxPages);
        set => SetExtensionValue(
            RecipeRuntimeSettings.PaginationMaxPagesTvSnapshotKey,
            Math.Clamp(
                value,
                RecipeRuntimeSettings.MinPaginationMaxPages,
                RecipeRuntimeSettings.MaxPaginationMaxPages).ToString());
    }

    public int PaginationMaxTotalResults
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.PaginationMaxTotalResultsKey,
            RecipeRuntimeSettings.DefaultPaginationMaxTotalResults,
            RecipeRuntimeSettings.MinPaginationMaxTotalResults,
            RecipeRuntimeSettings.MaxPaginationMaxTotalResults);
        set => SetExtensionValue(
            RecipeRuntimeSettings.PaginationMaxTotalResultsKey,
            Math.Clamp(
                value,
                RecipeRuntimeSettings.MinPaginationMaxTotalResults,
                RecipeRuntimeSettings.MaxPaginationMaxTotalResults).ToString());
    }

    public int PaginationIdleTimeoutSecondsMovie
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.PaginationIdleTimeoutSecondsMovieKey,
            RecipeRuntimeSettings.DefaultPaginationIdleTimeoutSecondsMovie,
            RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds,
            RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds);
        set => SetExtensionValue(
            RecipeRuntimeSettings.PaginationIdleTimeoutSecondsMovieKey,
            Math.Clamp(
                value,
                RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds,
                RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds).ToString());
    }

    public int PaginationIdleTimeoutSecondsTvParallel
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.PaginationIdleTimeoutSecondsTvParallelKey,
            RecipeRuntimeSettings.DefaultPaginationIdleTimeoutSecondsTvParallel,
            RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds,
            RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds);
        set => SetExtensionValue(
            RecipeRuntimeSettings.PaginationIdleTimeoutSecondsTvParallelKey,
            Math.Clamp(
                value,
                RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds,
                RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds).ToString());
    }

    public int PaginationIdleTimeoutSecondsTvSnapshot
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.PaginationIdleTimeoutSecondsTvSnapshotKey,
            RecipeRuntimeSettings.DefaultPaginationIdleTimeoutSecondsTvSnapshot,
            RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds,
            RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds);
        set => SetExtensionValue(
            RecipeRuntimeSettings.PaginationIdleTimeoutSecondsTvSnapshotKey,
            Math.Clamp(
                value,
                RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds,
                RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds).ToString());
    }

    public int SearchIdleTimeoutSecondsMovie
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.SearchIdleTimeoutSecondsMovieKey,
            RecipeRuntimeSettings.DefaultSearchIdleTimeoutSecondsMovie,
            RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds,
            RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds);
        set => SetExtensionValue(
            RecipeRuntimeSettings.SearchIdleTimeoutSecondsMovieKey,
            Math.Clamp(
                value,
                RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds,
                RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds).ToString());
    }

    public int SearchIdleTimeoutSecondsTvParallel
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.SearchIdleTimeoutSecondsTvParallelKey,
            RecipeRuntimeSettings.DefaultSearchIdleTimeoutSecondsTvParallel,
            RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds,
            RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds);
        set => SetExtensionValue(
            RecipeRuntimeSettings.SearchIdleTimeoutSecondsTvParallelKey,
            Math.Clamp(
                value,
                RecipeRuntimeSettings.MinSearchIdleTimeoutSeconds,
                RecipeRuntimeSettings.MaxSearchIdleTimeoutSeconds).ToString());
    }

    public string EpisodeNumberingMode
    {
        get => RecipeRuntimeSettings.NormalizeEpisodeNumberingMode(
            GetExtensionValue(RecipeRuntimeSettings.EpisodeNumberingModeKey));
        set => SetExtensionValue(
            RecipeRuntimeSettings.EpisodeNumberingModeKey,
            RecipeRuntimeSettings.NormalizeEpisodeNumberingMode(value));
    }

    public bool ShowPackExtrasPrioritySettings =>
        _recipeTargetKind == MediaKind.TvSeasonPack && _module.BlockType == RecipeBlockType.Scoring;

    public bool IsApplicableToTarget =>
        !(_recipeTargetKind == MediaKind.Movie && _module.BlockType == RecipeBlockType.CandidateParser);

    public bool ShowScoringWeightSettings => _module.BlockType == RecipeBlockType.Scoring;

    public int QualityWeight
    {
        get => GetScoringInt(RecipeRuntimeSettings.QualityWeightKey, CandidateScoringWeights.Default.QualityWeight, 0, 50_000_000);
        set => SetScoringInt(RecipeRuntimeSettings.QualityWeightKey, value, 0, 50_000_000);
    }

    public int AudioWeight
    {
        get => GetScoringInt(RecipeRuntimeSettings.AudioWeightKey, CandidateScoringWeights.Default.AudioWeight, 0, 10_000_000);
        set => SetScoringInt(RecipeRuntimeSettings.AudioWeightKey, value, 0, 10_000_000);
    }

    public int SeedersWeight
    {
        get => GetScoringInt(RecipeRuntimeSettings.SeedersWeightKey, CandidateScoringWeights.Default.SeedersWeight, 0, 10_000);
        set => SetScoringInt(RecipeRuntimeSettings.SeedersWeightKey, value, 0, 10_000);
    }

    public int SeedersCap
    {
        get => GetScoringInt(RecipeRuntimeSettings.SeedersCapKey, CandidateScoringWeights.Default.SeedersCap, 0, 500_000);
        set => SetScoringInt(RecipeRuntimeSettings.SeedersCapKey, value, 0, 500_000);
    }

    public int IdentityWeight
    {
        get => GetScoringInt(RecipeRuntimeSettings.IdentityWeightKey, CandidateScoringWeights.Default.IdentityWeight, 0, 10_000);
        set => SetScoringInt(RecipeRuntimeSettings.IdentityWeightKey, value, 0, 10_000);
    }

    public int EpisodeWeight
    {
        get => GetScoringInt(RecipeRuntimeSettings.EpisodeWeightKey, CandidateScoringWeights.Default.EpisodeWeight, 0, 100_000);
        set => SetScoringInt(RecipeRuntimeSettings.EpisodeWeightKey, value, 0, 100_000);
    }

    public int SizeWeight
    {
        get => GetScoringInt(RecipeRuntimeSettings.SizeWeightKey, CandidateScoringWeights.Default.SizeWeight, 0, 10_000_000);
        set => SetScoringInt(RecipeRuntimeSettings.SizeWeightKey, value, 0, 10_000_000);
    }

    public IReadOnlyList<string> SizePreferenceOptions => RecipeRuntimeSettings.SizePreferenceOptions;

    public string SizePreference
    {
        get => RecipeRuntimeSettings.NormalizeSizePreferenceOption(GetExtensionValue(RecipeRuntimeSettings.SizePreferenceKey));
        set => SetExtensionValue(RecipeRuntimeSettings.SizePreferenceKey, RecipeRuntimeSettings.NormalizeSizePreferenceOption(value));
    }

    public int SeasonMatchScorePerSeason
    {
        get => GetScoringInt(
            RecipeRuntimeSettings.SeasonMatchScorePerSeasonKey,
            CandidateScoringWeights.Default.SeasonMatchScorePerSeason,
            0,
            10_000);
        set => SetScoringInt(RecipeRuntimeSettings.SeasonMatchScorePerSeasonKey, value, 0, 10_000);
    }

    public int SingleSeasonBoost
    {
        get => GetScoringInt(RecipeRuntimeSettings.SingleSeasonBoostKey, CandidateScoringWeights.Default.SingleSeasonBoost, 0, 100_000);
        set => SetScoringInt(RecipeRuntimeSettings.SingleSeasonBoostKey, value, 0, 100_000);
    }

    public bool PackExtrasPriorityEnabled
    {
        get => GetExtensionBool(RecipeRuntimeSettings.PackExtrasPriorityEnabledKey, true);
        set => SetExtensionValue(RecipeRuntimeSettings.PackExtrasPriorityEnabledKey, value.ToString());
    }

    public int PackExtrasPriorityScore
    {
        get => GetExtensionInt(
            RecipeRuntimeSettings.PackExtrasPriorityScoreKey,
            RecipeRuntimeSettings.DefaultPackExtrasPriorityScore,
            0,
            50000);
        set => SetExtensionValue(
            RecipeRuntimeSettings.PackExtrasPriorityScoreKey,
            Math.Clamp(value, 0, 50000).ToString());
    }

    [RelayCommand(CanExecute = nameof(ShowScoringWeightSettings))]
    private void ResetScoringToDefaults()
    {
        RecipeRuntimeSettings.ApplyScoringDefaults(_module);
        NotifyStateChanged();
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
            $"Use library alt titles: {(UseLibraryEnglishTitles ? "Yes" : "No")}",
            $"Max library alt titles for search: {MaxLibraryAlternativeTitlesForSearch}",
            $"Aliases: {CountLines(AliasesText)}"
        ],
        RecipeBlockType.QueryBuilder =>
        [
            $"Enabled: {(IsEnabled ? "Yes" : "No")}",
            $"Qualities: {SelectedQualitiesSummary}",
            $"Preferred audio: {DisplayOrEmpty(PreferredAudioCodec)}",
            $"Query templates: {CountLines(QueryTemplatesText)}",
            $"Custom queries: {CountLines(CustomQueriesText)}",
            $"Skip default title: {(SkipDefaultTitle ? "Yes" : "No")}",
            $"Sanitize query: {(SanitizeQuery ? "Yes" : "No")}"
        ],
        RecipeBlockType.SearchSource =>
            BuildSearchSourceSummary(),
        RecipeBlockType.CandidateFilter =>
        [
            $"Enabled: {(IsEnabled ? "Yes" : "No")}",
            $"Qualities: {SelectedQualitiesSummary}",
            $"Minimum seeders: {MinimumSeeders}",
            $"Min size GB: {(MinimumSizeGb?.ToString() ?? "none")}",
            $"Max size GB: {(MaximumSizeGb?.ToString() ?? "none")}",
            $"Preferred audio: {DisplayOrEmpty(PreferredAudioCodec)}",
            $"Prefer terms: {CountLines(PreferTermsText)}",
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
        RecipeBlockType.Scoring => BuildScoringSummary(),
        _ => [$"Enabled: {(IsEnabled ? "Yes" : "No")}"]
    };

    private IReadOnlyList<string> BuildScoringSummary()
    {
        var lines = new List<string>
        {
            $"Enabled: {(IsEnabled ? "Yes" : "No")}",
            $"Quality weight: {QualityWeight:N0}",
            $"Audio weight: {AudioWeight:N0}",
            $"Seeders weight: {SeedersWeight}",
            $"Seeders cap: {SeedersCap:N0}",
            $"Identity weight: {IdentityWeight}",
            $"Episode-title weight: {EpisodeWeight}",
            $"Size weight: {SizeWeight:N0}",
            $"Size preference: {SizePreference}"
        };
        if (ShowPackExtrasPrioritySettings)
        {
            lines.Add($"Season match per season: {SeasonMatchScorePerSeason}");
            lines.Add($"Single-season boost: {SingleSeasonBoost:N0}");
            lines.Add($"Pack extras/OVA/special priority: {(PackExtrasPriorityEnabled ? "On" : "Off")}");
            lines.Add($"Pack name-match bonus: {PackExtrasPriorityScore:N0}");
        }

        return lines;
    }

    private IReadOnlyList<string> BuildSearchSourceSummary()
    {
        var lines = new List<string>
        {
            $"Enabled: {(IsEnabled ? "Yes" : "No")}",
            $"Mode: {SearchMode}",
            $"Candidate debug log: {(EnableCandidateDebugLog ? "On" : "Off")}",
            $"Search pagination: {(EnableSearchPagination ? "On" : "Off")}",
            $"Plugins: {DisplayOrEmpty(Plugins)}",
            $"Category: {DisplayOrEmpty(Category)}",
            $"Candidates per fetch: {MaxCandidatesPerFetch}",
            $"Deduplicate candidates: {(DeduplicateCandidates ? "On" : "Off")}",
            $"Fuzzy dedup: {(FuzzyDeduplicate ? "On" : "Off")}{(FuzzyDeduplicate ? $", size tolerance: {FuzzyDeduplicateSizeToleranceMb} MB" : string.Empty)}"
        };

        if (IsMovieTarget)
        {
            if (EnableSearchPagination)
            {
                lines.Add($"Pagination page size: {PaginationPageSize}");
                lines.Add($"Pagination max pages (movie): {PaginationMaxPagesMovie}");
                lines.Add($"Pagination max total results: {PaginationMaxTotalResults}");
                lines.Add($"Pagination idle timeout: {PaginationIdleTimeoutSecondsMovie}s{(PaginationIdleTimeoutSecondsMovie == 0 ? " (off)" : string.Empty)}");
                lines.Add("Result limit: hidden while pagination is on");
            }
            else
            {
                lines.Add($"Result limit: {ResultLimit}");
                lines.Add($"Search idle timeout: {SearchIdleTimeoutSecondsMovie}s{(SearchIdleTimeoutSecondsMovie == 0 ? " (off)" : string.Empty)}");
            }

            lines.Add($"Parallel searches: {ParallelSearchCount}");
            lines.Add($"Movie search timeout: {MovieSearchTimeoutSeconds}s");
            return lines;
        }

        if (IsSnapshotSearchMode)
        {
            lines.Add("Snapshot search: On");
            if (EnableSearchPagination)
            {
                lines.Add($"Pagination page size: {PaginationPageSize}");
                lines.Add($"Pagination max pages (TV snapshot): {PaginationMaxPagesTvSnapshot}");
                lines.Add($"Pagination max total results: {PaginationMaxTotalResults}");
                lines.Add($"Pagination idle timeout: {PaginationIdleTimeoutSecondsTvSnapshot}s{(PaginationIdleTimeoutSecondsTvSnapshot == 0 ? " (off)" : string.Empty)}");
            }

            lines.Add($"Snapshot target: {SnapshotTargetResults}");
            lines.Add($"Snapshot timeout: {SnapshotTimeoutSeconds}s");
            lines.Add($"Snapshot idle timeout: {SnapshotIdleTimeoutSeconds}s{(SnapshotIdleTimeoutSeconds == 0 ? " (off)" : string.Empty)}");
            lines.Add($"Local match workers: {LocalMatchWorkers}");
            return lines;
        }

        lines.Add("Snapshot search: Off");
        if (EnableSearchPagination)
        {
            lines.Add($"Pagination page size: {PaginationPageSize}");
            lines.Add($"Pagination max pages (TV parallel): {PaginationMaxPagesTvParallel}");
            lines.Add($"Pagination max total results: {PaginationMaxTotalResults}");
            lines.Add($"Pagination idle timeout: {PaginationIdleTimeoutSecondsTvParallel}s{(PaginationIdleTimeoutSecondsTvParallel == 0 ? " (off)" : string.Empty)}");
            lines.Add("Result limit: hidden while pagination is on");
        }
        else
        {
            lines.Add($"Result limit: {ResultLimit}");
            lines.Add($"Search idle timeout: {SearchIdleTimeoutSecondsTvParallel}s{(SearchIdleTimeoutSecondsTvParallel == 0 ? " (off)" : string.Empty)}");
        }
        lines.Add($"Parallel searches: {ParallelSearchCount}");
        lines.Add($"TV parallel search timeout: {ParallelSearchTimeoutSeconds}s");
        return lines;
    }

    private int GetScoringInt(string key, int defaultValue, int min, int max) =>
        GetExtensionInt(key, defaultValue, min, max);

    private void SetScoringInt(string key, int value, int min, int max) =>
        SetExtensionValue(key, Math.Clamp(value, min, max).ToString());

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
    public static IReadOnlyList<ModuleFieldHelpItem> GetItems(
        RecipeBlockType blockType,
        MediaKind targetKind,
        bool useShowSnapshotSearch) =>
        blockType switch
        {
            RecipeBlockType.Identity =>
            [
                new() { FieldName = "Enabled", Description = "Turns identity matching on or off for this recipe.", OutputImpact = "When disabled, only the primary library title is used. Aliases are ignored during search and candidate filtering." },
                new() { FieldName = "Use library alternative titles", Description = "Include TMDB English and Japanese romaji alternative titles stored on the tracked show or movie.", OutputImpact = "Adds those library alternative titles to {title} expansion and torrent title matching. Works with Skip default title." },
                new() { FieldName = "Max library alt titles for search", Description = "How many ranked TMDB library alternative titles to expand into search queries. Library UI may still show more.", OutputImpact = "Lower values run fewer snapshot and fetch queries. Titles are ranked by short romaji or abbreviations first; near-duplicate EN variants are skipped. 0 disables library alt expansion even when the checkbox above is enabled." },
                new() { FieldName = "Title aliases", Description = "Alternative names for the show or movie.", OutputImpact = "Each alias is used as an extra {title} variant in queries and helps accept torrents that use abbreviations or alternate spellings." }
            ],
            RecipeBlockType.QueryBuilder =>
            [
                new() { FieldName = "Quality allow list", Description = "Accepted quality labels such as 1080p, 1440p, or 2160p.", OutputImpact = "Generates one search query per quality token. More qualities mean more queries and broader search coverage." },
                new() { FieldName = "Preferred audio", Description = "Comma-separated audio labels to prefer, e.g. Dolby, DV, Atmos.", OutputImpact = "Inserted into query templates as {audio}. Each matching token in a candidate name adds a scoring boost." },
                new() { FieldName = "Query templates", Description = "Patterns sent to qBittorrent search.", OutputImpact = "Each template is expanded with title, year, season, episode, quality, and audio. More templates increase candidate discovery at the cost of more searches." },
                new() { FieldName = "Custom queries", Description = "Extra templates appended after the generated list, one entry per line.", OutputImpact = "Useful for manual search phrases that do not fit the standard templates. Each entry is expanded like a normal template." },
                new() { FieldName = "Skip default title", Description = "Exclude the library show or movie title from {title} expansion.", OutputImpact = "When enabled, the primary library title is skipped. Identity aliases and library alternative titles (English and romaji, when enabled) are still used for {title}." },
                new() { FieldName = "Sanitize special characters in query", Description = "Strip punctuation such as :, ,, ?, !, quotes, and parentheses from the rendered query before sending it to qBittorrent.", OutputImpact = "Keeps letters, digits, spaces, and hyphens. Prevents some search plugins that use punctuation as URL delimiters from truncating or corrupting the search. Recommended: on." }
            ],
            RecipeBlockType.SearchSource =>
                BuildSearchSourceHelp(targetKind, useShowSnapshotSearch),
            RecipeBlockType.CandidateFilter =>
            [
                new() { FieldName = "Quality", Description = "Allowed quality labels for accepted candidates, including 1440p when selected.", OutputImpact = "Torrents that do not match any listed quality are rejected before scoring." },
                new() { FieldName = "Minimum seeders", Description = "Lowest seeder count still accepted.", OutputImpact = "Higher values reduce dead or slow torrents but may eliminate rare releases." },
                new() { FieldName = "Min size GB", Description = "Optional lower size floor in gigabytes. 0 disables the floor. Unknown/missing sizes are not rejected.", OutputImpact = "Rejects tiny same-quality encodes below the floor while keeping max size as the hard ceiling." },
                new() { FieldName = "Max size GB", Description = "Optional upper size limit in gigabytes.", OutputImpact = "Oversized packs or remuxes are rejected when set." },
                new() { FieldName = "Preferred audio", Description = "Comma-separated audio labels used for scoring bonus, e.g. Dolby, DV.", OutputImpact = "Does not reject candidates. Each matching token adds a scoring boost (more matches = higher rank)." },
                new() { FieldName = "Prefer terms", Description = "Custom soft-preference terms, one per line (same boost mechanic as preferred audio).", OutputImpact = "Does not reject candidates. Each matching term raises the score using the audio weight." },
                new() { FieldName = "Include terms", Description = "Terms that must appear in the torrent name.", OutputImpact = "Useful to require WEB-DL, x265, or a specific language tag." },
                new() { FieldName = "Exclude terms", Description = "Terms that reject a candidate immediately.", OutputImpact = "Common use: block cam, telesync, or unwanted codecs." },
                new() { FieldName = "Preferred release groups", Description = "Groups you prefer when ranking.", OutputImpact = "Currently informational for scoring; blocked groups always reject." },
                new() { FieldName = "Blocked release groups", Description = "Groups that are always rejected.", OutputImpact = "Any matching group name in the torrent title removes the candidate." }
            ],
            RecipeBlockType.CandidateParser =>
                targetKind == MediaKind.Movie
                    ? [new()
                    {
                        FieldName = "Not used for movies",
                        Description = "Movie recipes do not use Candidate Parser settings.",
                        OutputImpact = "This module is hidden in Movie target to avoid confusion."
                    }]
                    :
                    [
                        new() { FieldName = "Episode numbering", Description = "Controls whether filenames use Standard TV SxxEyy numbering or Anime absolute numbering such as One Piece - 1163.", OutputImpact = "Standard TV keeps existing behavior. Anime absolute accepts absolute episode numbers for shows that publish that way." },
                        new() { FieldName = "Probe candidate metadata", Description = "Download torrent metadata to verify episode/year coverage.", OutputImpact = "Improves accuracy for ambiguous filenames but adds extra qBittorrent requests." }
                    ],
            RecipeBlockType.Scoring =>
            [
                new() { FieldName = "Quality weight", Description = "Multiplier applied to the detected quality rank (2160p down to 480p).", OutputImpact = "Higher values make resolution differences dominate the final score." },
                new() { FieldName = "Audio weight", Description = "Multiplier for preferred audio matches and Prefer terms matches from the Candidate Filter module.", OutputImpact = "Raises releases that match preferred audio and/or custom prefer terms without rejecting others." },
                new() { FieldName = "Seeders weight", Description = "Points added per seeder, up to the cap below.", OutputImpact = "Higher values favor well-seeded torrents over marginal quality or title matches." },
                new() { FieldName = "Seeders cap", Description = "Maximum seeder count counted toward score.", OutputImpact = "Prevents extremely large swarms from overwhelming other factors." },
                new() { FieldName = "Identity / title-match weight", Description = "Multiplier for matched show or movie title and alias tokens.", OutputImpact = "Helps torrents with stronger title matches beat vague or abbreviated names." },
                new() { FieldName = "Episode-title weight", Description = "Multiplier for matched episode title tokens in the filename.", OutputImpact = "Useful when multiple candidates share the same episode number but differ in embedded episode title." },
                new() { FieldName = "Size weight", Description = "Multiplier for the size preference score (0–100). Default sits below audio and far below quality.", OutputImpact = "Adds a soft size boost without overriding quality or preferred audio." },
                new() { FieldName = "Size preference", Description = "Prefer larger, prefer smaller, or Off. Uses Candidate Filter Min/Max size as the soft range.", OutputImpact = "Prefer larger boosts bigger files in range; prefer smaller does the inverse; Off disables size boost." },
                new() { FieldName = "Season match score per season", Description = "Pack only. Points per selected season covered by the torrent.", OutputImpact = "Rewards packs that cover more of the seasons you selected." },
                new() { FieldName = "Single-season boost", Description = "Pack only. Flat bonus when the torrent covers exactly one season.", OutputImpact = "Slightly prefers focused single-season packs over multi-season bundles when other factors are close." },
                new() { FieldName = "Pack extras / OVA / special priority", Description = "Pack only. Enables bonus scoring when the torrent name mentions OVA, special, extra, OAD, or similar.", OutputImpact = "Complete bundles (season + extras) rank above season-only packs." },
                new() { FieldName = "Pack name-match bonus", Description = "Pack only. Extra points when extras/OVA/special keywords are found in the torrent display name.", OutputImpact = "Higher values make complete bundles win more often." },
                new() { FieldName = "Use defaults", Description = "Resets all Scoring weights to built-in system values.", OutputImpact = "Clears custom tuning so the recipe behaves like a fresh default install." }
            ],
            _ =>
            [
                new() { FieldName = "Module", Description = "Recipe pipeline step.", OutputImpact = "Each enabled module contributes to how searches run, candidates are filtered, and torrents are added." }
            ]
        };

    private static IReadOnlyList<ModuleFieldHelpItem> BuildSearchSourceHelp(
        MediaKind targetKind,
        bool useShowSnapshotSearch)
    {
        var items = new List<ModuleFieldHelpItem>
        {
            new() { FieldName = "Plugins", Description = "Which indexer plugins to query. 'enabled' uses all active plugins.", OutputImpact = "Limits or broadens which indexers contribute candidates to the result set." },
            new() { FieldName = "Category", Description = "qBittorrent search category filter.", OutputImpact = "Narrows results to TV, movies, or all categories depending on plugin support." },
            new() { FieldName = "Candidates per fetch", Description = "How many accepted candidates are kept after scoring.", OutputImpact = "Lower values reduce noise; higher values keep more backup options." },
            new() { FieldName = "Deduplicate candidates", Description = "Remove duplicate torrent URLs before ranking, keeping the copy with the most seeders.", OutputImpact = "Frees candidate slots for distinct torrents when the same release appears from multiple indexers." },
            new() { FieldName = "Fuzzy deduplicate", Description = "Also group candidates by normalized filename and file size bucket.", OutputImpact = "Removes near-duplicate releases that use different tracker URLs but represent the same torrent." },
            new() { FieldName = "Fuzzy dedup size tolerance", Description = "File size bucket width in MB for fuzzy dedup. 0 means exact byte size only.", OutputImpact = "Larger values treat small size differences across trackers as the same release; smaller values are stricter." },
            new() { FieldName = "Enable search pagination", Description = "Fetch qBittorrent search results in multiple pages (limit + offset) instead of only the first page.", OutputImpact = "Improves recall for late or deep results; can increase qB calls and runtime if caps are high." },
            new() { FieldName = "Pagination page size", Description = "Rows fetched per page window from qBittorrent.", OutputImpact = "Higher page size reduces page count but can increase per-call payload." },
            new() { FieldName = "Pagination max pages (movie)", Description = "Maximum pages fetched per movie query when pagination is enabled.", OutputImpact = "Raises movie recall with bounded runtime and API call count." },
            new() { FieldName = "Pagination max pages (TV parallel)", Description = "Maximum pages fetched per episode query in TV parallel mode.", OutputImpact = "Controls call explosion risk in per-episode mode. Keep conservative for large queues." },
            new() { FieldName = "Pagination max pages (TV snapshot)", Description = "Maximum pages fetched per query in TV snapshot mode.", OutputImpact = "Adds modest snapshot depth with bounded overhead." },
            new() { FieldName = "Pagination max total results", Description = "Hard cap on merged unique rows kept per query after pagination dedup.", OutputImpact = "Prevents runaway memory and scoring cost even when indexers return large pools." },
            new() { FieldName = "Pagination idle timeout", Description = "End the search early when merged unique results stop growing for N seconds. 0 disables this rule.", OutputImpact = "Cuts wait time once indexers stop producing new unique rows." },
            new() { FieldName = "Result limit", Description = "Maximum rows returned per query.", OutputImpact = "Higher values surface more candidates but increase search time and noise." },
            new() { FieldName = "Search idle timeout", Description = "Non-pagination early stop when merged unique results stop growing for N seconds. 0 disables.", OutputImpact = "Shortens long polls in non-pagination mode while keeping hard timeout as safety." },
            new() { FieldName = "Parallel searches", Description = "How many queries run at the same time.", OutputImpact = "Faster execution when set higher, but may hit qBittorrent search capacity limits." }
        };

        if (targetKind == MediaKind.Movie)
        {
            items.Add(new()
            {
                FieldName = "Movie search timeout",
                Description = "Maximum seconds to wait for each movie query before the app stops polling qBittorrent results.",
                OutputImpact = "Higher timeout can capture late-arriving indexer rows; lower timeout returns faster."
            });
            items.Add(new()
            {
                FieldName = "Write candidate debug log",
                Description = "Recipe-driven per-run debug file with accepted and rejected rows plus reasons.",
                OutputImpact = "When enabled, each Run Cart using this recipe writes cart-debug files to the logs folder for troubleshooting."
            });
            return items;
        }

        items.Add(new()
        {
            FieldName = "TV parallel search timeout",
            Description = "Maximum seconds to wait for each per-episode TV query in parallel search mode.",
            OutputImpact = "Higher timeout improves coverage for slower indexers in TV parallel mode."
        });
        items.Add(new()
        {
            FieldName = "Search mode",
            Description = "Choose TV parallel per-episode search or TV snapshot search.",
            OutputImpact = "Snapshot mode captures one large show result set and matches locally; parallel mode searches each episode directly."
        });
        items.Add(new()
        {
            FieldName = "Write candidate debug log",
            Description = "Recipe-driven per-run debug file with accepted and rejected rows plus reasons.",
            OutputImpact = "When enabled, each Run Cart using this recipe writes cart-debug files to the logs folder for troubleshooting."
        });

        if (useShowSnapshotSearch)
        {
            items.Add(new()
            {
                FieldName = "Snapshot target results",
                Description = "How many rows to collect in TV snapshot mode.",
                OutputImpact = "Larger snapshots improve coverage but take longer to finish."
            });
            items.Add(new()
            {
                FieldName = "Snapshot timeout",
                Description = "Maximum seconds to wait for TV snapshot search completion.",
                OutputImpact = "Prevents hung snapshot polling from blocking the job."
            });
            items.Add(new()
            {
                FieldName = "Snapshot idle timeout",
                Description = "Stop snapshot polling when no new results arrive for this many seconds. 0 disables early stop.",
                OutputImpact = "Finishes sooner when indexers stop returning new rows."
            });
            items.Add(new()
            {
                FieldName = "Local match workers",
                Description = "Parallel workers that match snapshot rows to episodes.",
                OutputImpact = "More workers speed up matching on large season sets."
            });
        }

        return items;
    }
}
