using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using media_management_app.Services;
using media_management_app.ViewModels;

namespace media_management_app;

public partial class MainWindow : Window
{
    private const double NormalCornerRadius = 8;
    private static readonly Thickness NormalShellBorderThickness = new(1);
    private static readonly Thickness NormalResizeBorderThickness = new(6);

    private readonly ISettingsService _settingsService;
    private readonly ITrayIconService _trayIconService;
    private readonly IAppLifecycleService _lifecycleService;
    private bool _allowClose;
    private bool _chromeIsMaximized;
    private WindowState _previousWindowState = WindowState.Normal;

    public MainWindow(
        MainViewModel viewModel,
        ISettingsService settingsService,
        ITrayIconService trayIconService,
        IAppLifecycleService lifecycleService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settingsService = settingsService;
        _trayIconService = trayIconService;
        _lifecycleService = lifecycleService;
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UpdateWindowChromeForState();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        var wasMinimized = _previousWindowState == WindowState.Minimized;
        _previousWindowState = WindowState;
        base.OnStateChanged(e);
        UpdateWindowChromeForState();
        UpdateLifecycleForWindowState(wasMinimized);
    }

    private void UpdateWindowChromeForState()
    {
        if (WindowChrome.GetWindowChrome(this) is not WindowChrome chrome)
        {
            return;
        }

        var isMaximized = WindowState == WindowState.Maximized;
        // Guard: skip the property mutations if already in the correct state to avoid
        // triggering unnecessary WPF chrome invalidation and layout passes.
        if (isMaximized == _chromeIsMaximized)
        {
            return;
        }

        _chromeIsMaximized = isMaximized;
        chrome.CornerRadius = isMaximized
            ? new CornerRadius(0)
            : new CornerRadius(NormalCornerRadius);
        chrome.ResizeBorderThickness = isMaximized
            ? new Thickness(0)
            : NormalResizeBorderThickness;
        ShellBorder.BorderThickness = isMaximized
            ? new Thickness(0)
            : NormalShellBorderThickness;
    }

    private void UpdateLifecycleForWindowState(bool wasMinimized)
    {
        if (WindowState == WindowState.Minimized && !ShouldUseTray())
        {
            _lifecycleService.EnterBackgroundMode();
            return;
        }

        // Only signal foreground when genuinely restoring from minimized state,
        // not on Normal↔Maximized transitions which are not lifecycle events.
        if (wasMinimized && WindowState != WindowState.Minimized && IsVisible)
        {
            _lifecycleService.EnterForegroundMode();
        }
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
