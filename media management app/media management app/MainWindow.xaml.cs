using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using media_management_app.Services;
using media_management_app.ViewModels;

namespace media_management_app;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IFetchJobService _fetchJobService;

    public MainWindow(MainViewModel viewModel, IFetchJobService fetchJobService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _fetchJobService = fetchJobService;
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

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_fetchJobService.HasActiveJobs())
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            "Auto Torrent has running or queued fetch jobs. Closing the app will cancel them. Do you want to exit?",
            "Cancel fetch jobs?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        _fetchJobService.CancelActiveJobs();
    }
}
