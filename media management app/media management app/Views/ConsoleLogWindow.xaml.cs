using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class ConsoleLogWindow : Window, IConsoleLogWindow
{
    private readonly IAppLogger _logger;
    private readonly ConsoleLogViewModel _viewModel;
    private ScrollViewer? _prettyScrollViewer;
    private bool _allowClose;

    public ConsoleLogWindow(IAppLogger logger, ISettingsService settingsService)
    {
        InitializeComponent();
        _logger = logger;
        _viewModel = new ConsoleLogViewModel(logger, settingsService);
        DataContext = _viewModel;
        _logger.UiLogs.CollectionChanged += UiLogs_CollectionChanged;
        ((INotifyCollectionChanged)_viewModel.EntriesView).CollectionChanged += EntriesView_CollectionChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        PrettyLogList.Loaded += PrettyLogList_Loaded;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _logger.UiLogs.CollectionChanged -= UiLogs_CollectionChanged;
        ((INotifyCollectionChanged)_viewModel.EntriesView).CollectionChanged -= EntriesView_CollectionChanged;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Dispose();
        base.OnClosing(e);
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void UiLogs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_logger.UiLogs.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var lastLogLine = _logger.UiLogs[^1];
            ConsoleLogList.ScrollIntoView(lastLogLine);
        });
    }

    private void ConsoleLogList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CopySelectedLogs();
            e.Handled = true;
        }
    }

    private void CopySelectedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedLogs();
    }

    private void CopyAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CopyLogs(_logger.UiLogs);
    }

    private void CopySelectedLogs()
    {
        var selectedLogs = ConsoleLogList.SelectedItems.Cast<string>().ToList();
        if (selectedLogs.Count == 0)
        {
            return;
        }

        CopyLogs(selectedLogs);
    }

    private static void CopyLogs(IEnumerable<string> logs)
    {
        var text = string.Join(Environment.NewLine, logs);
        if (!string.IsNullOrWhiteSpace(text))
        {
            System.Windows.Clipboard.SetText(text);
        }
    }

    #region Pretty mode

    private void PrettyLogList_Loaded(object sender, RoutedEventArgs e) => ScrollPrettyToEnd();

    // The list's template is only applied once it is visible, so the ScrollViewer is looked up lazily.
    private ScrollViewer? EnsurePrettyScrollViewer()
    {
        if (_prettyScrollViewer is not null)
        {
            return _prettyScrollViewer;
        }

        _prettyScrollViewer = FindDescendant<ScrollViewer>(PrettyLogList);
        if (_prettyScrollViewer is not null)
        {
            _prettyScrollViewer.ScrollChanged += PrettyScrollViewer_ScrollChanged;
        }

        return _prettyScrollViewer;
    }

    private bool IsPrettyAtBottom
    {
        get
        {
            var scrollViewer = EnsurePrettyScrollViewer();
            return scrollViewer is null || scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 1;
        }
    }

    private void EntriesView_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_viewModel.IsPrettyMode)
        {
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ScrollPrettyToEnd();
            return;
        }

        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        // Offsets still describe the layout before this item, so this is "was the user at the bottom".
        if (IsPrettyAtBottom)
        {
            ScrollPrettyToEnd();
        }
        else
        {
            _viewModel.NotifyUnseenEntry();
        }
    }

    private void PrettyScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_viewModel.IsPaused && IsPrettyAtBottom)
        {
            _viewModel.ResetUnseenEntries();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConsoleLogViewModel.IsPrettyMode) && _viewModel.IsPrettyMode)
        {
            ScrollPrettyToEnd();
        }
    }

    private void ScrollPrettyToEnd()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (PrettyLogList.Items.Count > 0)
            {
                PrettyLogList.ScrollIntoView(PrettyLogList.Items[^1]);
            }

            EnsurePrettyScrollViewer()?.ScrollToEnd();
        });
    }

    private void JumpToBottom_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsPaused = false;
        _viewModel.ResetUnseenEntries();
        ScrollPrettyToEnd();
    }

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.ApplySearchNow();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.SearchText = string.Empty;
            _viewModel.ApplySearchNow();
            e.Handled = true;
        }
    }

    private void PrettyLogList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CopyEntries(PrettyLogList.SelectedItems.Cast<AppLogEntry>());
            e.Handled = true;
        }
    }

    private void PrettyCopySelectedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CopyEntries(PrettyLogList.SelectedItems.Cast<AppLogEntry>());
    }

    private void PrettyCopyVisibleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CopyEntries(_viewModel.EntriesView.Cast<AppLogEntry>());
    }

    // Copies use the classic line format so pasted logs read the same in either mode.
    private static void CopyEntries(IEnumerable<AppLogEntry> entries)
    {
        CopyLogs(entries.OrderBy(entry => entry.Sequence).Select(AppLogger.Format));
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    #endregion
}
