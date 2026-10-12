using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class QueryTemplateItemViewModel : ObservableObject
{
    private readonly MediaKind _targetKind;
    private readonly QueryTemplateDraft _draft;

    public QueryTemplateItemViewModel(string pattern, MediaKind targetKind, bool committed)
    {
        _targetKind = targetKind;
        _draft = new QueryTemplateDraft(pattern, committed);
        RefreshValidation();
    }

    public MediaKind TargetKind => _targetKind;

    public string Pattern
    {
        get => _draft.Pattern;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_draft.Pattern, next, StringComparison.Ordinal))
            {
                return;
            }

            _draft.Pattern = next;
            OnPropertyChanged();
            RefreshValidation();
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(DirtyMark));
        }
    }

    public string? CommittedPattern => _draft.CommittedPattern;

    public bool IsCommitted => _draft.IsCommitted;

    public bool IsDirty => _draft.IsDirty;

    public string DirtyMark => IsDirty ? "*" : string.Empty;

    [ObservableProperty]
    private bool isRejected;

    [ObservableProperty]
    private string warningText = string.Empty;

    public string ListLabel => string.IsNullOrWhiteSpace(Pattern) ? "(empty)" : Pattern;

    public void Save()
    {
        _draft.Save();
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsCommitted));
        OnPropertyChanged(nameof(CommittedPattern));
        OnPropertyChanged(nameof(DirtyMark));
    }

    public void Reset()
    {
        var previous = _draft.Pattern;
        _draft.Reset();
        if (string.Equals(previous, _draft.Pattern, StringComparison.Ordinal))
        {
            return;
        }

        OnPropertyChanged(nameof(Pattern));
        RefreshValidation();
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DirtyMark));
    }

    private void RefreshValidation()
    {
        if (string.IsNullOrWhiteSpace(Pattern))
        {
            IsRejected = false;
            WarningText = string.Empty;
            OnPropertyChanged(nameof(ListLabel));
            return;
        }

        var result = QueryTokenCatalog.Validate(Pattern.Trim(), _targetKind);
        IsRejected = !result.IsAccepted;
        WarningText = result.IsAccepted ? string.Empty : result.Reason ?? "Invalid template";
        OnPropertyChanged(nameof(ListLabel));
    }
}

public sealed class QueryInsertChip
{
    public QueryInsertChip(
        string label,
        string insertText,
        string? displayBrushKey = null,
        bool isExpansion = false)
    {
        Label = label;
        InsertText = insertText;
        DisplayBrushKey = displayBrushKey;
        IsExpansion = isExpansion;
    }

    public string Label { get; }

    public string InsertText { get; }

    public string? DisplayBrushKey { get; }

    public bool IsExpansion { get; }
}

public sealed partial class QueryTemplateBuilderDialogViewModel : ObservableObject
{
    private const string DefaultAddPattern = "{title} {quality}";
    private readonly SearchRecipe _recipe;

    public QueryTemplateBuilderDialogViewModel(SearchRecipe recipe, IReadOnlyList<string> templates)
    {
        _recipe = recipe;
        TargetKind = recipe.TargetKind;
        TokenButtons = QueryTokenCatalog.Enabled
            .Where(token => token.ApplicableKinds.Contains(recipe.TargetKind))
            .Select(token => new QueryInsertChip(
                token.DisplayName,
                token.Placeholder,
                token.DisplayBrushKey,
                token.Expansion != QueryTokenExpansion.None))
            .ToList();
        LiteralChips =
        [
            new QueryInsertChip("S", "S"),
            new QueryInsertChip("E", "E"),
            new QueryInsertChip("x", "x"),
            new QueryInsertChip("-", "-"),
            new QueryInsertChip("complete", "complete"),
            new QueryInsertChip("pack", "pack"),
            new QueryInsertChip("season", "season")
        ];

        Items = [];
        foreach (var template in QueryTokenCatalog.NormalizeTemplates(templates))
        {
            AddItem(template, select: false, committed: true);
        }

        if (Items.Count == 0)
        {
            AddItem(DefaultAddPattern, select: true, committed: false);
        }

        SelectedItem = Items.FirstOrDefault();
        RefreshDerived();
    }

    public MediaKind TargetKind { get; }

    public ObservableCollection<QueryTemplateItemViewModel> Items { get; }

    public IReadOnlyList<QueryInsertChip> TokenButtons { get; }

    public IReadOnlyList<QueryInsertChip> LiteralChips { get; }

    [ObservableProperty]
    private QueryTemplateItemViewModel? selectedItem;

    [ObservableProperty]
    private int caretIndex;

    [ObservableProperty]
    private string estimateText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<QueryTemplatePreviewLine> previewLines = [];

    public bool CanAdd => Items.Count < QueryTokenCatalog.MaxTemplates;

    public bool HasPatternEditor => SelectedItem is not null;

    public string CountLabel => $"{Items.Count:00}/ {QueryTokenCatalog.MaxTemplates}";

    public IReadOnlyList<string> WorkingTemplates =>
        QueryTokenCatalog.NormalizeTemplates(Items.Select(item => item.Pattern));

    public IReadOnlyList<string> ResultTemplates =>
        QueryTokenCatalog.NormalizeTemplates(
            Items
                .Where(item => item.IsCommitted && !string.IsNullOrWhiteSpace(item.CommittedPattern))
                .Select(item => item.CommittedPattern!));

    partial void OnSelectedItemChanged(QueryTemplateItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasPatternEditor));
        NotifyItemCommands();
        InsertChipCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private void AddItem()
    {
        if (!CanAdd)
        {
            return;
        }

        AddItem(DefaultAddPattern, select: true, committed: false);
        RefreshDerived();
    }

    [RelayCommand(CanExecute = nameof(CanDuplicateItem))]
    private void DuplicateItem()
    {
        if (SelectedItem is null || !CanAdd)
        {
            return;
        }

        AddItem(SelectedItem.Pattern, select: true, committed: false);
        RefreshDerived();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private void DeleteItem()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var index = Items.IndexOf(SelectedItem);
        SelectedItem.PropertyChanged -= OnItemPropertyChanged;
        Items.Remove(SelectedItem);
        SelectedItem = Items.Count == 0
            ? null
            : Items[Math.Clamp(index, 0, Items.Count - 1)];
        RefreshDerived();
    }

    [RelayCommand(CanExecute = nameof(CanSaveItem))]
    private void SaveItem()
    {
        SelectedItem?.Save();
        RefreshDerived();
    }

    [RelayCommand(CanExecute = nameof(CanResetItem))]
    private void ResetItem()
    {
        SelectedItem?.Reset();
        RefreshDerived();
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var index = Items.IndexOf(SelectedItem);
        if (index <= 0)
        {
            return;
        }

        Items.Move(index, index - 1);
        RefreshDerived();
    }

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var index = Items.IndexOf(SelectedItem);
        if (index < 0 || index >= Items.Count - 1)
        {
            return;
        }

        Items.Move(index, index + 1);
        RefreshDerived();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private void InsertChip(QueryInsertChip? chip)
    {
        if (chip is null || SelectedItem is null)
        {
            return;
        }

        var pattern = SelectedItem.Pattern ?? string.Empty;
        var index = Math.Clamp(CaretIndex, 0, pattern.Length);
        SelectedItem.Pattern = pattern.Insert(index, chip.InsertText);
        CaretIndex = index + chip.InsertText.Length;
        CaretMoved?.Invoke(this, CaretIndex);
        RefreshDerived();
    }

    public event EventHandler<int>? CaretMoved;

    private bool CanAddItem() => CanAdd;

    private bool CanDuplicateItem() => CanAdd && SelectedItem is not null;

    private bool HasSelectedItem() => SelectedItem is not null;

    private bool CanSaveItem() => SelectedItem is { IsDirty: true };

    private bool CanResetItem() => SelectedItem is { IsDirty: true };

    private bool CanMoveUp() => SelectedItem is not null && Items.IndexOf(SelectedItem) > 0;

    private bool CanMoveDown() =>
        SelectedItem is not null && Items.IndexOf(SelectedItem) is var index && index >= 0 && index < Items.Count - 1;

    private void AddItem(string pattern, bool select, bool committed)
    {
        var item = new QueryTemplateItemViewModel(pattern, TargetKind, committed);
        item.PropertyChanged += OnItemPropertyChanged;
        Items.Add(item);
        if (select)
        {
            SelectedItem = item;
        }
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QueryTemplateItemViewModel.Pattern)
            or nameof(QueryTemplateItemViewModel.ListLabel)
            or nameof(QueryTemplateItemViewModel.IsDirty)
            or "")
        {
            RefreshDerived();
        }
    }

    private void RefreshDerived()
    {
        var patterns = WorkingTemplates;
        var estimate = QuerySearchEstimator.Estimate(_recipe, patterns);
        EstimateText = QuerySearchEstimator.FormatHuntEstimate(estimate);
        var grouped = QueryTemplatePreview.PreviewShortLongByTemplate(_recipe, patterns);
        PreviewLines = grouped.Count == 0
            ? [new QueryTemplatePreviewLine { Text = "(no valid templates)", IsPairStart = false }]
            : grouped;
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CountLabel));
        NotifyItemCommands();
    }

    private void NotifyItemCommands()
    {
        AddItemCommand.NotifyCanExecuteChanged();
        DuplicateItemCommand.NotifyCanExecuteChanged();
        DeleteItemCommand.NotifyCanExecuteChanged();
        SaveItemCommand.NotifyCanExecuteChanged();
        ResetItemCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
}
