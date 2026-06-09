using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
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

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizedState();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            RestoreWindowForDrag(e);
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizedState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizedState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void RestoreWindowForDrag(MouseButtonEventArgs e)
    {
        if (WindowState != WindowState.Maximized)
        {
            return;
        }

        var mousePosition = PointToScreen(e.GetPosition(this));
        var restoredWidth = RestoreBounds.Width;
        var restoredHeight = RestoreBounds.Height;
        var horizontalRatio = mousePosition.X / ActualWidth;

        WindowState = WindowState.Normal;
        Width = restoredWidth;
        Height = restoredHeight;
        Left = mousePosition.X - (restoredWidth * horizontalRatio);
        Top = Math.Max(0, mousePosition.Y - 20);
    }
}
