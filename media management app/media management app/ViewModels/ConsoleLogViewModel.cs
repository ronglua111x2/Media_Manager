using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

/// <summary>
/// State for the console log window. Classic mode binds <see cref="UiLogs"/> exactly as before;
/// Pretty mode binds <see cref="EntriesView"/> (level filter, search, pause/freeze, clear view).
/// </summary>
public sealed partial class ConsoleLogViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(200);

    private readonly IAppLogger _logger;
    private readonly ISettingsService _settingsService;
    private readonly ListCollectionView _entriesView;
    private readonly DispatcherTimer _searchTimer;
    private readonly int[] _levelCounts = new int[Enum.GetValues<AppLogLevel>().Length];
    private bool _isLoading;
    private long _clearedThroughSequence;
    private long _pausedAtSequence = long.MaxValue;
    private string _appliedSearchText = string.Empty;

    public ConsoleLogViewModel(IAppLogger logger, ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;

        _isLoading = true;
        var logs = settingsService.Current.Logs;
        var hidden = logs.ConsoleHiddenLevels ?? [];
        IsPrettyMode = logs.ConsoleMode == ConsoleViewMode.Pretty;
        IsWrapEnabled = logs.ConsoleWrap;
        ShowTrace = !hidden.Contains(AppLogLevel.Trace);
        ShowDebug = !hidden.Contains(AppLogLevel.Debug);
        ShowInfo = !hidden.Contains(AppLogLevel.Info);
        ShowWarning = !hidden.Contains(AppLogLevel.Warning);
        ShowError = !hidden.Contains(AppLogLevel.Error);
        _isLoading = false;

        _entriesView = new ListCollectionView(logger.UiEntries) { Filter = FilterEntry };
        ((INotifyCollectionChanged)_entriesView).CollectionChanged += EntriesView_CollectionChanged;

        _searchTimer = new DispatcherTimer { Interval = SearchDebounce };
        _searchTimer.Tick += SearchTimer_Tick;

        RecountLevels();
        logger.UiEntries.CollectionChanged += UiEntries_CollectionChanged;
    }

    public ObservableCollection<string> UiLogs => _logger.UiLogs;

    public ICollectionView EntriesView => _entriesView;

    public bool IsClassicMode
    {
        get => !IsPrettyMode;
        set
        {
            if (value)
            {
                IsPrettyMode = false;
            }
        }
    }

    [ObservableProperty]
    private bool isPrettyMode;

    [ObservableProperty]
    private bool isWrapEnabled;

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private bool showTrace;

    [ObservableProperty]
    private bool showDebug;

    [ObservableProperty]
    private bool showInfo;

    [ObservableProperty]
    private bool showWarning;

    [ObservableProperty]
    private bool showError;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewEntries))]
    private int newEntryCount;

    public bool HasNewEntries => NewEntryCount > 0;

    public int TraceCount => _levelCounts[(int)AppLogLevel.Trace];

    public int DebugCount => _levelCounts[(int)AppLogLevel.Debug];

    public int InfoCount => _levelCounts[(int)AppLogLevel.Info];

    public int WarningCount => _levelCounts[(int)AppLogLevel.Warning];

    public int ErrorCount => _levelCounts[(int)AppLogLevel.Error] + _levelCounts[(int)AppLogLevel.Critical];

    public int VisibleCount => _entriesView.Count;

    public int TotalCount => _levelCounts.Sum();

    public bool IsFiltered =>
        !(ShowTrace && ShowDebug && ShowInfo && ShowWarning && ShowError) ||
        !string.IsNullOrWhiteSpace(_appliedSearchText);

    public string StatusText
    {
        get
        {
            var text = $"{VisibleCount} / {TotalCount} lines";
            if (IsFiltered)
            {
                text += " · filtered";
            }

            if (IsPaused)
            {
                text += " · paused";
            }

            return text;
        }
    }

    /// <summary>
    /// Called by the view when an entry arrives that the user cannot see (paused or scrolled up).
    /// </summary>
    public void NotifyUnseenEntry() => NewEntryCount++;

    public void ResetUnseenEntries() => NewEntryCount = 0;

    /// <summary>Applies the pending search text immediately (skips the debounce).</summary>
    public void ApplySearchNow()
    {
        _searchTimer.Stop();
        _appliedSearchText = SearchText.Trim();
        RefreshView();
    }

    [RelayCommand]
    private void ClearView()
    {
        var entries = _logger.UiEntries;
        _clearedThroughSequence = entries.Count == 0 ? _clearedThroughSequence : entries[^1].Sequence;
        if (IsPaused)
        {
            _pausedAtSequence = _clearedThroughSequence;
        }

        ResetUnseenEntries();
        RecountLevels();
        RefreshView();
    }

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    public void Dispose()
    {
        _searchTimer.Stop();
        _searchTimer.Tick -= SearchTimer_Tick;
        _logger.UiEntries.CollectionChanged -= UiEntries_CollectionChanged;
        ((INotifyCollectionChanged)_entriesView).CollectionChanged -= EntriesView_CollectionChanged;
        _entriesView.DetachFromSourceCollection();
    }

    partial void OnIsPrettyModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsClassicMode));
        SaveSettings();
    }

    partial void OnIsWrapEnabledChanged(bool value) => SaveSettings();

    partial void OnIsPausedChanged(bool value)
    {
        var entries = _logger.UiEntries;
        _pausedAtSequence = value
            ? (entries.Count == 0 ? _clearedThroughSequence : entries[^1].Sequence)
            : long.MaxValue;

        if (!value)
        {
            ResetUnseenEntries();
            RefreshView();
        }

        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnShowTraceChanged(bool value) => OnLevelFilterChanged();

    partial void OnShowDebugChanged(bool value) => OnLevelFilterChanged();

    partial void OnShowInfoChanged(bool value) => OnLevelFilterChanged();

    partial void OnShowWarningChanged(bool value) => OnLevelFilterChanged();

    partial void OnShowErrorChanged(bool value) => OnLevelFilterChanged();

    partial void OnSearchTextChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnLevelFilterChanged()
    {
        if (_isLoading)
        {
            return;
        }

        RefreshView();
        SaveSettings();
    }

    private bool FilterEntry(object item)
    {
        if (item is not AppLogEntry entry ||
            entry.Sequence <= _clearedThroughSequence ||
            entry.Sequence > _pausedAtSequence ||
            !IsLevelVisible(entry.Level))
        {
            return false;
        }

        var search = _appliedSearchText;
        return search.Length == 0 ||
               entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.SourceFileName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.MemberName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               (entry.ExceptionText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private bool IsLevelVisible(AppLogLevel level) => level switch
    {
        AppLogLevel.Trace => ShowTrace,
        AppLogLevel.Debug => ShowDebug,
        AppLogLevel.Info => ShowInfo,
        AppLogLevel.Warning => ShowWarning,
        _ => ShowError
    };

    private void SearchTimer_Tick(object? sender, EventArgs e) => ApplySearchNow();

    private void RefreshView()
    {
        _entriesView.Refresh();
        RaiseCountsChanged();
    }

    private void UiEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                AdjustCounts(e.NewItems, +1);
                break;
            case NotifyCollectionChangedAction.Remove:
                AdjustCounts(e.OldItems, -1);
                break;
            default:
                RecountLevels();
                return;
        }

        if (IsPaused && e.Action == NotifyCollectionChangedAction.Add)
        {
            NewEntryCount += e.NewItems?.Count ?? 0;
        }

        RaiseCountsChanged();
    }

    private void EntriesView_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(StatusText));
    }

    private void AdjustCounts(System.Collections.IList? items, int delta)
    {
        if (items is null)
        {
            return;
        }

        foreach (AppLogEntry entry in items)
        {
            if (entry.Sequence > _clearedThroughSequence)
            {
                _levelCounts[(int)entry.Level] += delta;
            }
        }
    }

    private void RecountLevels()
    {
        Array.Clear(_levelCounts);
        foreach (var entry in _logger.UiEntries)
        {
            if (entry.Sequence > _clearedThroughSequence)
            {
                _levelCounts[(int)entry.Level]++;
            }
        }

        RaiseCountsChanged();
    }

    private void RaiseCountsChanged()
    {
        OnPropertyChanged(nameof(TraceCount));
        OnPropertyChanged(nameof(DebugCount));
        OnPropertyChanged(nameof(InfoCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(StatusText));
    }

    private void SaveSettings()
    {
        if (_isLoading)
        {
            return;
        }

        var logs = _settingsService.Current.Logs;
        logs.ConsoleMode = IsPrettyMode ? ConsoleViewMode.Pretty : ConsoleViewMode.Classic;
        logs.ConsoleWrap = IsWrapEnabled;
        logs.ConsoleHiddenLevels = Enum.GetValues<AppLogLevel>()
            .Where(level => level != AppLogLevel.Critical && !IsLevelVisible(level))
            .ToList();

        try
        {
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Saving console view settings failed: {ex.Message}", LogTarget.File);
        }
    }
}
