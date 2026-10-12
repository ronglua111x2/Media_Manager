using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using media_management_app.Services;

namespace media_management_app.Views;

public partial class ConsoleLogWindow : Window, IConsoleLogWindow
{
    private readonly IAppLogger _logger;
    private bool _allowClose;

    public ConsoleLogWindow(IAppLogger logger)
    {
        InitializeComponent();
        _logger = logger;
        DataContext = logger;
        _logger.UiLogs.CollectionChanged += UiLogs_CollectionChanged;
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
}
