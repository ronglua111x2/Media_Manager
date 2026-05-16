using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using media_management_app.Models;
using media_management_app.ViewModels;

namespace media_management_app;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.UiLogs.CollectionChanged += UiLogs_CollectionChanged;
    }

    private void UiLogs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel.UiLogs.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var lastLogLine = _viewModel.UiLogs[^1];
            ConsoleLogList.ScrollIntoView(lastLogLine);
        });
    }

    private void InboxGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid { DataContext: InboxViewModel inboxViewModel } dataGrid)
        {
            return;
        }

        inboxViewModel.UpdateSelectedItems(dataGrid.SelectedItems.OfType<SourceItem>());
    }
}
