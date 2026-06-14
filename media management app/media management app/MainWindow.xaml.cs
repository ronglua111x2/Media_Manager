using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using media_management_app.Services;
using media_management_app.ViewModels;

namespace media_management_app;

public partial class MainWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly ITrayIconService _trayIconService;
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel, ISettingsService settingsService, ITrayIconService trayIconService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settingsService = settingsService;
        _trayIconService = trayIconService;
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && _settingsService.Current.Startup.CloseToTray)
        {
            e.Cancel = true;
            EnsureTray();
            _trayIconService.HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        System.Windows.Application.Current.Shutdown();
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
        if (ShouldUseTray())
        {
            EnsureTray();
            _trayIconService.HideToTray();
            return;
        }

        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizedState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_settingsService.Current.Startup.CloseToTray)
        {
            CloseForShutdown();
            return;
        }

        Close();
    }

    private bool ShouldUseTray()
    {
        var startup = _settingsService.Current.Startup;
        return startup.StartMinimized || startup.CloseToTray;
    }

    private void EnsureTray()
    {
        if (!_trayIconService.IsInitialized)
        {
            _trayIconService.Initialize(this);
        }
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
