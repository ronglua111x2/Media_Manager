using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using MahApps.Metro.IconPacks;

namespace media_management_app.Views;

public partial class WebViewerWindow : Window
{
    private const double NormalCornerRadius = 8;
    private static readonly Thickness NormalShellBorderThickness = new(1);
    private static readonly Thickness NormalResizeBorderThickness = new(6);

    private bool _chromeIsMaximized;
    private bool _isHtmlFullscreen;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;

    public WebViewerWindow()
    {
        InitializeComponent();
    }

    public WebViewerWindow(string title, PackIconLucideKind iconKind, string brandBrushKey)
        : this()
    {
        ApplyIdentity(title, iconKind, brandBrushKey);
    }

    public event EventHandler? ReloadRequested;

    public System.Windows.Controls.Grid BrowserHostPanel => BrowserHost;

    public void SetCurrentUrl(string? url)
    {
        UrlText.Text = url ?? string.Empty;
    }

    public void SetHtmlFullscreen(bool fullscreen)
    {
        if (_isHtmlFullscreen == fullscreen)
        {
            return;
        }

        _isHtmlFullscreen = fullscreen;
        TitleBar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;

        if (fullscreen)
        {
            _stateBeforeFullscreen = WindowState;
            WindowState = WindowState.Maximized;
            return;
        }

        if (WindowState == WindowState.Maximized && _stateBeforeFullscreen != WindowState.Maximized)
        {
            WindowState = _stateBeforeFullscreen;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UpdateWindowChromeForState();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateWindowChromeForState();
    }

    private void ApplyIdentity(string title, PackIconLucideKind iconKind, string brandBrushKey)
    {
        Title = title;
        TitleLabel.Text = title;
        BrandIcon.Kind = iconKind;
        if (TryFindResource(brandBrushKey) is System.Windows.Media.Brush brandBrush)
        {
            BrandBadge.Background = brandBrush;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_isHtmlFullscreen)
        {
            return;
        }

        if (e.Key == Key.F5 || (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control))
        {
            RaiseReloadRequested();
            e.Handled = true;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isHtmlFullscreen || ShouldIgnoreTitleBarDrag(e.OriginalSource))
        {
            return;
        }

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

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseReloadRequested();
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

    private void RaiseReloadRequested() => ReloadRequested?.Invoke(this, EventArgs.Empty);

    private bool ShouldIgnoreTitleBarDrag(object originalSource)
    {
        if (originalSource is not DependencyObject current)
        {
            return false;
        }

        while (current is not null)
        {
            if (ReferenceEquals(current, ReloadButton))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void ToggleMaximizedState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateWindowChromeForState()
    {
        if (WindowChrome.GetWindowChrome(this) is not WindowChrome chrome)
        {
            return;
        }

        var isMaximized = WindowState == WindowState.Maximized;
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
