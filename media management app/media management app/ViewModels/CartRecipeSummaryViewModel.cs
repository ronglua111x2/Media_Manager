using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class CartRecipeSummaryViewModel : ObservableObject
{
    private readonly CartRecipeOverrideSet _overrides;
    private readonly Action<CartRecipeOverrideSet> _persist;
    private bool _isInitializing;

    public CartRecipeSummaryViewModel(
        SearchRecipe recipe,
        AutoTorrentSettings settings,
        CartRecipeOverrideSet? storedOverrides,
        Action<CartRecipeOverrideSet> persist)
    {
        Recipe = recipe;
        _persist = persist;
        _overrides = storedOverrides ?? new CartRecipeOverrideSet();
        RecipeMaxCandidates = RecipeRuntimeSettings.GetMaxCandidatesPerFetch(recipe, settings);
        RecipeMinSeeders = GetModule(recipe, RecipeBlockType.CandidateFilter)?.MinimumSeeders ?? 0;
        RecipeMinSizeGb = CartRecipeOverrideSet.BytesToGb(GetModule(recipe, RecipeBlockType.CandidateFilter)?.MinimumSizeBytes);
        RecipeCandidateDebug = RecipeRuntimeSettings.GetEnableCandidateDebugLog(recipe);
        QualitySummary = BuildQualitySummary(recipe);
        MinimumSeedersSummary = FormatSeeders(RecipeMinSeeders);
        MinimumSizeSummary = FormatSize(RecipeMinSizeGb);
        SearchModeSummary = BuildSearchModeSummary(recipe, settings);
        PluginsSummary = BuildPluginsSummary(recipe);
        CandidateDebugSummary = RecipeCandidateDebug ? "On" : "Off";
        TitleBehaviorSummary = BuildTitleBehaviorSummary(recipe);

        _isInitializing = true;
        IsMaxCandidatesOverridden = _overrides.MaxCandidates?.Enabled == true;
        OverrideMaxCandidates = ClampMaxCandidates(_overrides.MaxCandidates?.Value ?? RecipeMaxCandidates);
        IsMinSeedersOverridden = _overrides.MinSeeders?.Enabled == true;
        OverrideMinSeeders = ClampMinSeeders(_overrides.MinSeeders?.Value ?? RecipeMinSeeders);
        IsMinSizeOverridden = _overrides.MinSizeGb?.Enabled == true;
        OverrideMinSizeGbText = CartRecipeOverrideSet.FormatGb(_overrides.MinSizeGb?.Value ?? RecipeMinSizeGb);
        IsCandidateDebugOverridden = _overrides.CandidateDebug?.Enabled == true;
        OverrideCandidateDebug = _overrides.CandidateDebug?.Value ?? RecipeCandidateDebug;
        _isInitializing = false;
    }

    public SearchRecipe Recipe { get; }

    public string Name => Recipe.Name;

    public MediaKind TargetKind => Recipe.TargetKind;

    public string TargetLabel => Recipe.TargetKind switch
    {
        MediaKind.TvSeasonPack => "Pack",
        MediaKind.Movie => "Movie",
        _ => "Episode"
    };

    public int RecipeMaxCandidates { get; }

    public int RecipeMinSeeders { get; }

    public double RecipeMinSizeGb { get; }

    public bool RecipeCandidateDebug { get; }

    public string QualitySummary { get; }

    public string MinimumSeedersSummary { get; }

    public string MinimumSizeSummary { get; }

    public string SearchModeSummary { get; }

    public string PluginsSummary { get; }

    public string CandidateDebugSummary { get; }

    public string EffectiveCandidateDebugSummary =>
        (IsCandidateDebugOverridden ? OverrideCandidateDebug : RecipeCandidateDebug) ? "On" : "Off";

    public string TitleBehaviorSummary { get; }

    public bool HasAnyOverride => _overrides.HasAnyEnabled;

    public RecipeExecutionOverrides? ToExecutionOverrides() => _overrides.ToExecutionOverrides();

    [ObservableProperty]
    private bool isMaxCandidatesOverridden;

    [ObservableProperty]
    private int overrideMaxCandidates;

    [ObservableProperty]
    private bool isMinSeedersOverridden;

    [ObservableProperty]
    private int overrideMinSeeders;

    [ObservableProperty]
    private bool isMinSizeOverridden;

    [ObservableProperty]
    private string overrideMinSizeGbText = "0";

    [ObservableProperty]
    private bool isCandidateDebugOverridden;

    [ObservableProperty]
    private bool overrideCandidateDebug;

    [RelayCommand]
    private void ToggleMaxCandidatesOverride() =>
        ToggleIntOverride(
            () => _overrides.MaxCandidates,
            value => _overrides.MaxCandidates = value,
            RecipeMaxCandidates,
            ClampMaxCandidates,
            enabled => IsMaxCandidatesOverridden = enabled,
            value => OverrideMaxCandidates = value);

    [RelayCommand]
    private void ToggleMinSeedersOverride() =>
        ToggleIntOverride(
            () => _overrides.MinSeeders,
            value => _overrides.MinSeeders = value,
            RecipeMinSeeders,
            ClampMinSeeders,
            enabled => IsMinSeedersOverridden = enabled,
            value => OverrideMinSeeders = value);

    [RelayCommand]
    private void ToggleMinSizeOverride()
    {
        _overrides.MinSizeGb ??= new CartDoubleOverride
        {
            Enabled = false,
            Value = ClampMinSizeGb(RecipeMinSizeGb)
        };
        _overrides.MinSizeGb.Enabled = !_overrides.MinSizeGb.Enabled;
        _overrides.MinSizeGb.Value = ClampMinSizeGb(_overrides.MinSizeGb.Value);
        OverrideMinSizeGbText = CartRecipeOverrideSet.FormatGb(_overrides.MinSizeGb.Value);
        IsMinSizeOverridden = _overrides.MinSizeGb.Enabled;
        Persist();
    }

    [RelayCommand]
    private void CycleCandidateDebug()
    {
        var displayed = IsCandidateDebugOverridden ? OverrideCandidateDebug : RecipeCandidateDebug;
        _overrides.CandidateDebug ??= new CartBoolOverride
        {
            Enabled = false,
            Value = RecipeCandidateDebug
        };
        _overrides.CandidateDebug.Enabled = true;
        _overrides.CandidateDebug.Value = !displayed;
        OverrideCandidateDebug = _overrides.CandidateDebug.Value;
        IsCandidateDebugOverridden = true;
        Persist();
    }

    [RelayCommand]
    private void ToggleCandidateDebugOverride()
    {
        _overrides.CandidateDebug ??= new CartBoolOverride
        {
            Enabled = false,
            Value = RecipeCandidateDebug
        };
        _overrides.CandidateDebug.Enabled = !_overrides.CandidateDebug.Enabled;
        OverrideCandidateDebug = _overrides.CandidateDebug.Value;
        IsCandidateDebugOverridden = _overrides.CandidateDebug.Enabled;
        Persist();
    }

    partial void OnOverrideMaxCandidatesChanged(int value)
    {
        if (_isInitializing || _overrides.MaxCandidates is not { Enabled: true } stored)
        {
            return;
        }

        var normalized = ClampMaxCandidates(value);
        if (normalized != value)
        {
            OverrideMaxCandidates = normalized;
            return;
        }

        stored.Value = normalized;
        Persist();
    }

    partial void OnOverrideMinSeedersChanged(int value)
    {
        if (_isInitializing || _overrides.MinSeeders is not { Enabled: true } stored)
        {
            return;
        }

        var normalized = ClampMinSeeders(value);
        if (normalized != value)
        {
            OverrideMinSeeders = normalized;
            return;
        }

        stored.Value = normalized;
        Persist();
    }

    partial void OnOverrideMinSizeGbTextChanged(string value)
    {
        if (_isInitializing || _overrides.MinSizeGb is not { Enabled: true } stored)
        {
            return;
        }

        if (!TryParseMinSizeGb(value, out var parsed))
        {
            return;
        }

        stored.Value = parsed;
        var formatted = CartRecipeOverrideSet.FormatGb(parsed);
        if (formatted != value)
        {
            OverrideMinSizeGbText = formatted;
            return;
        }

        Persist();
    }

    partial void OnOverrideCandidateDebugChanged(bool value)
    {
        if (_isInitializing || _overrides.CandidateDebug is not { Enabled: true } stored)
        {
            return;
        }

        stored.Value = value;
        Persist();
        OnPropertyChanged(nameof(EffectiveCandidateDebugSummary));
    }

    private void ToggleIntOverride(
        Func<CartIntOverride?> get,
        Action<CartIntOverride> set,
        int recipeValue,
        Func<int, int> clamp,
        Action<bool> setEnabled,
        Action<int> setValue)
    {
        var current = get() ?? new CartIntOverride
        {
            Enabled = false,
            Value = clamp(recipeValue)
        };
        current.Enabled = !current.Enabled;
        current.Value = clamp(current.Value);
        set(current);
        setValue(current.Value);
        setEnabled(current.Enabled);
        Persist();
    }

    private void Persist()
    {
        if (_isInitializing)
        {
            return;
        }

        _persist(_overrides);
        OnPropertyChanged(nameof(HasAnyOverride));
        OnPropertyChanged(nameof(EffectiveCandidateDebugSummary));
    }

    private static int ClampMaxCandidates(int value) =>
        Math.Clamp(
            value,
            RecipeRuntimeSettings.MinCartMaxCandidatesOverride,
            RecipeRuntimeSettings.MaxCartMaxCandidatesOverride);

    private static int ClampMinSeeders(int value) =>
        Math.Clamp(
            value,
            RecipeRuntimeSettings.MinCartMinSeedersOverride,
            RecipeRuntimeSettings.MaxCartMinSeedersOverride);

    private static double ClampMinSizeGb(double value) =>
        Math.Clamp(
            value,
            RecipeRuntimeSettings.MinCartMinSizeGbOverride,
            RecipeRuntimeSettings.MaxCartMinSizeGbOverride);

    private static bool TryParseMinSizeGb(string? text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ||
            double.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed))
        {
            value = ClampMinSizeGb(parsed);
            return true;
        }

        value = 0;
        return false;
    }

    private static RecipeModuleConfig? GetModule(SearchRecipe recipe, RecipeBlockType blockType) =>
        recipe.Modules.FirstOrDefault(module => module.BlockType == blockType && module.IsEnabled);

    private static string FormatSeeders(int minimum) =>
        minimum <= 0 ? "No minimum" : minimum.ToString(CultureInfo.InvariantCulture);

    private static string FormatSize(double gb) =>
        gb <= 0 ? "No minimum" : $"{CartRecipeOverrideSet.FormatGb(gb)} GB";

    private static string BuildQualitySummary(SearchRecipe recipe)
    {
        var qualities = GetModule(recipe, RecipeBlockType.CandidateFilter)?.QualityAllowList ?? [];
        return qualities.Count == 0 ? "Any quality" : string.Join(", ", qualities);
    }

    private static string BuildSearchModeSummary(SearchRecipe recipe, AutoTorrentSettings settings)
    {
        var mode = recipe.TargetKind switch
        {
            MediaKind.TvSeasonPack => "Pack snapshot",
            MediaKind.Movie => "Movie search",
            _ => RecipeRuntimeSettings.GetUseShowSnapshotSearch(recipe, settings)
                ? "TV snapshot"
                : "TV parallel"
        };
        return $"{mode} · Pagination {(RecipeRuntimeSettings.GetEnableSearchPagination(recipe) ? "on" : "off")}";
    }

    private static string BuildPluginsSummary(SearchRecipe recipe)
    {
        var raw = GetModule(recipe, RecipeBlockType.SearchSource)?.Plugins;
        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("enabled", StringComparison.OrdinalIgnoreCase))
        {
            return "All enabled";
        }

        var plugins = raw
            .Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (plugins.Count <= 3)
        {
            return string.Join(", ", plugins);
        }

        return $"{string.Join(", ", plugins.Take(3))} +{plugins.Count - 3}";
    }

    private static string BuildTitleBehaviorSummary(SearchRecipe recipe)
    {
        var identity = GetModule(recipe, RecipeBlockType.Identity);
        var query = GetModule(recipe, RecipeBlockType.QueryBuilder);
        var parts = new List<string>
        {
            RecipeRuntimeSettings.GetSkipDefaultTitle(query) ? "Primary skipped" : "Primary included"
        };

        if (RecipeRuntimeSettings.GetUseLibraryEnglishTitles(identity))
        {
            parts.Add($"Library titles ≤{RecipeRuntimeSettings.GetMaxLibraryAlternativeTitlesForSearch(identity)}");
        }

        if (identity?.Aliases.Count > 0)
        {
            parts.Add($"{identity.Aliases.Count} alias{(identity.Aliases.Count == 1 ? string.Empty : "es")}");
        }

        parts.Add(RecipeRuntimeSettings.GetSanitizeQuery(query) ? "Sanitized" : "Raw query");
        return string.Join(" · ", parts);
    }
}
