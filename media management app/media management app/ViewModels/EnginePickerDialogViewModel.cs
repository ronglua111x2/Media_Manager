using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class EnginePickerDialogViewModel : ObservableObject
{
    private readonly HashSet<string> _rememberedCustomNames = new(StringComparer.OrdinalIgnoreCase);

    public EnginePickerDialogViewModel(
        EnginePickerMode mode,
        IReadOnlyList<SearchPluginInfo> plugins,
        bool useAllEnabled,
        IReadOnlyList<string> selectedNames,
        IReadOnlyList<string>? lastCustomNames,
        EnginePriorityMode priorityMode,
        IReadOnlyList<IReadOnlyList<string>> rankGroups,
        bool isStaleList)
    {
        Mode = mode;
        IsStaleList = isStaleList;
        _useAllEnabled = useAllEnabled && mode == EnginePickerMode.Search;
        _isRankedMode = priorityMode == EnginePriorityMode.Ranked && mode == EnginePickerMode.Quality;

        plugins ??= [];
        selectedNames ??= [];
        rankGroups ??= [];

        foreach (var name in lastCustomNames ?? selectedNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _rememberedCustomNames.Add(name.Trim());
            }
        }

        var selectedSet = BuildInitialSelection(selectedNames, lastCustomNames);
        var rankLookup = BuildRankLookup(rankGroups);
        Items = new ObservableCollection<EnginePickerItemViewModel>(
            plugins.Select(plugin =>
            {
                var selected = selectedSet.Contains(plugin.Name);
                var rankGroup = rankLookup.TryGetValue(plugin.Name, out var rank) ? rank : 1;
                var item = new EnginePickerItemViewModel(
                    plugin.Name,
                    plugin.FullName,
                    plugin.Enabled,
                    selected,
                    rankGroup);
                item.SelectionChanged += (_, _) => OnItemSelectionChanged(item);
                item.RankChanged += (_, _) => RefreshSummaries();
                return item;
            }));

        PriorityItems = new ObservableCollection<EnginePickerItemViewModel>();
        if (_isRankedMode && IsQualityMode)
        {
            AssignUniqueSequentialRanks();
        }

        SyncPriorityItems();
        UpdateItemPickEnabled();
        RefreshSummaries();
    }

    public EnginePickerMode Mode { get; }

    public bool IsStaleList { get; }

    public bool IsSearchMode => Mode == EnginePickerMode.Search;

    public bool IsQualityMode => Mode == EnginePickerMode.Quality;

    public string TitleText => Mode == EnginePickerMode.Search ? "Choose search engines" : "Prefer search engines";

    public string HelpText => Mode == EnginePickerMode.Search
        ? "Select which qBittorrent search plugins this recipe should query. Only enabled plugins can be selected."
        : "Choose engines to prefer when ranking candidates. Ranked mode applies stronger boosts to higher ranks when Engine weight is set in Scoring.";

    public string StaleBannerText => IsStaleList
        ? "Showing the last known plugin list. Connect to qBittorrent WebUI and refresh if this looks outdated."
        : string.Empty;

    public bool ShowStaleBanner => IsStaleList;

    public ObservableCollection<EnginePickerItemViewModel> Items { get; }

    public ObservableCollection<EnginePickerItemViewModel> PriorityItems { get; }

    private bool _useAllEnabled;

    public bool UseAllEnabled
    {
        get => _useAllEnabled;
        set
        {
            if (!SetProperty(ref _useAllEnabled, value))
            {
                return;
            }

            if (IsSearchMode)
            {
                if (value)
                {
                    RememberCurrentCustomSelection();
                }
                else
                {
                    RestoreRememberedCustomSelection();
                }
            }

            OnPropertyChanged(nameof(IndividualPickEnabled));
            OnPropertyChanged(nameof(ShowIndividualPickHint));
            UpdateItemPickEnabled();
            RefreshSummaries();
        }
    }

    private bool _isRankedMode;

    public bool IsRankedMode
    {
        get => _isRankedMode;
        set
        {
            if (!SetProperty(ref _isRankedMode, value))
            {
                return;
            }

            if (value && IsQualityMode)
            {
                AssignUniqueSequentialRanks();
            }

            OnPropertyChanged(nameof(ShowRankPanel));
            SyncPriorityItems();
            RefreshSummaries();
        }
    }

    public bool ShowRankPanel => IsQualityMode && IsRankedMode;

    public bool IndividualPickEnabled => !IsSearchMode || !UseAllEnabled;

    public bool ShowIndividualPickHint => IsSearchMode && UseAllEnabled;

    public string IndividualPickHint =>
        "Individual picks are locked while All enabled is on. Your last custom selection is shown as a preview.";

    private EnginePickerItemViewModel? _selectedPriorityItem;

    public EnginePickerItemViewModel? SelectedPriorityItem
    {
        get => _selectedPriorityItem;
        set => SetProperty(ref _selectedPriorityItem, value);
    }

    private string _selectionSummary = string.Empty;

    public string SelectionSummary
    {
        get => _selectionSummary;
        private set => SetProperty(ref _selectionSummary, value);
    }

    public bool ResultUseAllEnabled => UseAllEnabled;

    public IReadOnlyList<string> ResultSelectedNames =>
        Items.Where(item => item.IsSelected && item.IsEnabled).Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<string> ResultLastCustomNames
    {
        get
        {
            if (UseAllEnabled)
            {
                return _rememberedCustomNames.Count > 0
                    ? _rememberedCustomNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList()
                    : ResultSelectedNames;
            }

            return ResultSelectedNames;
        }
    }

    public EnginePriorityMode ResultPriorityMode => IsRankedMode ? EnginePriorityMode.Ranked : EnginePriorityMode.Flat;

    public IReadOnlyList<IReadOnlyList<string>> ResultRankGroups
    {
        get
        {
            var selected = Items.Where(item => item.IsSelected && item.IsEnabled).ToList();
            if (selected.Count == 0)
            {
                return [];
            }

            if (!IsRankedMode)
            {
                return [selected.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList()];
            }

            return selected
                .OrderBy(item => item.RankGroup)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => (IReadOnlyList<string>)[item.Name])
                .ToList();
        }
    }

    public bool Validate(out string? errorMessage)
    {
        if (Mode == EnginePickerMode.Search && !UseAllEnabled && ResultSelectedNames.Count == 0)
        {
            errorMessage = "Select at least one enabled engine, or choose All enabled plugins.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    public void MoveSelectedUp()
    {
        if (SelectedPriorityItem is null || !IsRankedMode)
        {
            return;
        }

        var ordered = GetOrderedPriorityItems();
        var index = ordered.IndexOf(SelectedPriorityItem);
        if (index <= 0)
        {
            return;
        }

        SwapRanks(ordered[index], ordered[index - 1]);
        FinishRankMutation();
    }

    public void MoveSelectedDown()
    {
        if (SelectedPriorityItem is null || !IsRankedMode)
        {
            return;
        }

        var ordered = GetOrderedPriorityItems();
        var index = ordered.IndexOf(SelectedPriorityItem);
        if (index < 0 || index >= ordered.Count - 1)
        {
            return;
        }

        SwapRanks(ordered[index], ordered[index + 1]);
        FinishRankMutation();
    }

    public void MoveSelectedToTop()
    {
        if (SelectedPriorityItem is null || !IsRankedMode)
        {
            return;
        }

        var ordered = GetOrderedPriorityItems();
        var index = ordered.IndexOf(SelectedPriorityItem);
        if (index <= 0)
        {
            return;
        }

        for (var i = index; i > 0; i--)
        {
            SwapRanks(ordered[i], ordered[i - 1]);
        }

        FinishRankMutation();
    }

    public void MoveSelectedToBottom()
    {
        if (SelectedPriorityItem is null || !IsRankedMode)
        {
            return;
        }

        var ordered = GetOrderedPriorityItems();
        var index = ordered.IndexOf(SelectedPriorityItem);
        if (index < 0 || index >= ordered.Count - 1)
        {
            return;
        }

        for (var i = index; i < ordered.Count - 1; i++)
        {
            SwapRanks(ordered[i], ordered[i + 1]);
        }

        FinishRankMutation();
    }

    private void UpdateItemPickEnabled()
    {
        var enabled = IndividualPickEnabled;
        foreach (var item in Items)
        {
            item.SetParentPickEnabled(enabled);
        }
    }

    private HashSet<string> BuildInitialSelection(
        IReadOnlyList<string> selectedNames,
        IReadOnlyList<string>? lastCustomNames)
    {
        if (Mode == EnginePickerMode.Search && UseAllEnabled)
        {
            var preview = lastCustomNames is { Count: > 0 } ? lastCustomNames : selectedNames;
            return preview
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return selectedNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void OnItemSelectionChanged(EnginePickerItemViewModel item)
    {
        if (IsSearchMode && UseAllEnabled)
        {
            return;
        }

        if (IsSearchMode && item.IsSelected)
        {
            UseAllEnabled = false;
        }

        if (item.IsSelected && IsQualityMode && IsRankedMode)
        {
            item.RankGroup = PriorityItems.Count > 0
                ? PriorityItems.Max(priorityItem => priorityItem.RankGroup) + 1
                : 1;
        }

        RememberCurrentCustomSelection();
        SyncPriorityItems();
        RefreshSummaries();
    }

    private void RememberCurrentCustomSelection()
    {
        _rememberedCustomNames.Clear();
        foreach (var name in Items.Where(item => item.IsSelected && item.IsEnabled).Select(item => item.Name))
        {
            _rememberedCustomNames.Add(name);
        }
    }

    private void RestoreRememberedCustomSelection()
    {
        if (_rememberedCustomNames.Count == 0)
        {
            return;
        }

        foreach (var item in Items)
        {
            item.IsSelected = item.IsEnabled && _rememberedCustomNames.Contains(item.Name);
        }

        if (IsQualityMode && IsRankedMode)
        {
            AssignUniqueSequentialRanks();
        }

        SyncPriorityItems();
        RefreshSummaries();
    }

    private void SyncPriorityItems()
    {
        var ordered = Items
            .Where(item => item.IsSelected && item.IsEnabled)
            .OrderBy(item => item.RankGroup)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            var target = ordered[index];
            if (index >= PriorityItems.Count)
            {
                PriorityItems.Add(target);
                continue;
            }

            if (ReferenceEquals(PriorityItems[index], target))
            {
                continue;
            }

            var existingIndex = PriorityItems.IndexOf(target);
            if (existingIndex >= 0)
            {
                PriorityItems.Move(existingIndex, index);
            }
            else
            {
                PriorityItems.Insert(index, target);
            }
        }

        while (PriorityItems.Count > ordered.Count)
        {
            PriorityItems.RemoveAt(PriorityItems.Count - 1);
        }
    }

    private void AssignUniqueSequentialRanks()
    {
        var rank = 1;
        foreach (var item in Items
                     .Where(item => item.IsSelected && item.IsEnabled)
                     .OrderBy(item => item.RankGroup)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            item.RankGroup = rank++;
        }
    }

    private void NormalizeRankGroups()
    {
        var orderedGroups = PriorityItems
            .Select(item => item.RankGroup)
            .Distinct()
            .OrderBy(rank => rank)
            .Select((rank, index) => (Rank: rank, Normalized: index + 1))
            .ToDictionary(pair => pair.Rank, pair => pair.Normalized);

        foreach (var item in PriorityItems)
        {
            item.RankGroup = orderedGroups.TryGetValue(item.RankGroup, out var normalized) ? normalized : item.RankGroup;
        }
    }

    private List<EnginePickerItemViewModel> GetOrderedPriorityItems() =>
        PriorityItems
            .OrderBy(item => item.RankGroup)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void SwapRanks(EnginePickerItemViewModel left, EnginePickerItemViewModel right)
    {
        (left.RankGroup, right.RankGroup) = (right.RankGroup, left.RankGroup);
    }

    private void FinishRankMutation()
    {
        var selected = SelectedPriorityItem;
        NormalizeRankGroups();
        SyncPriorityItems();
        SelectedPriorityItem = selected;
        RefreshSummaries();
    }

    private void RefreshSummaries()
    {
        if (Mode == EnginePickerMode.Search)
        {
            SelectionSummary = UseAllEnabled
                ? BuildAllEnabledSummary()
                : string.Join(", ", ResultSelectedNames);
            return;
        }

        SelectionSummary = ResultRankGroups.Count == 0
            ? "None"
            : IsRankedMode
                ? string.Join(" > ", ResultRankGroups.Select(group => group.Count > 0 ? group[0] : string.Empty).Where(name => name.Length > 0))
                : string.Join(", ", ResultSelectedNames);
    }

    private string BuildAllEnabledSummary()
    {
        var preview = _rememberedCustomNames.Count > 0
            ? string.Join(", ", _rememberedCustomNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            : string.Join(", ", ResultSelectedNames);
        return string.IsNullOrWhiteSpace(preview)
            ? "All enabled plugins"
            : $"All enabled plugins (last custom: {preview})";
    }

    private static Dictionary<string, int> BuildRankLookup(IReadOnlyList<IReadOnlyList<string>> rankGroups)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rankGroups.Count; index++)
        {
            var group = rankGroups[index];
            if (group is null)
            {
                continue;
            }

            foreach (var name in group)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                lookup[name] = index + 1;
            }
        }

        return lookup;
    }
}
