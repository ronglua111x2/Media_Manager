using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MahApps.Metro.IconPacks;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.ViewModels;
using WinForms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfControl = System.Windows.Controls.Control;
using WpfPoint = System.Windows.Point;

namespace media_management_app.Services;

public sealed class TrayIconService : ITrayIconService
{
    private const string AppName = "Media Manager";
    private const string DefaultTrayTooltip = AppName;
    private const string BackgroundModeTrayTooltip = $"{AppName} - Background Mode";

    private readonly IAppLifecycleService _lifecycleService;
    private readonly ISettingsService _settingsService;
    private readonly IWorkspaceNavigator _workspaceNavigator;

    private MainWindow? _window;
    private WinForms.NotifyIcon? _notifyIcon;
    private ContextMenu? _contextMenu;
    private bool _disposed;

    public TrayIconService(
        IAppLifecycleService lifecycleService,
        ISettingsService settingsService,
        IWorkspaceNavigator workspaceNavigator)
    {
        _lifecycleService = lifecycleService;
        _settingsService = settingsService;
        _workspaceNavigator = workspaceNavigator;
        _lifecycleService.AppModeChanged += OnAppModeChanged;
    }

    public bool IsInitialized => _notifyIcon is not null;

    public void Initialize(MainWindow window)
    {
        if (_notifyIcon is not null)
        {
            _window = window;
            UpdateTrayTooltip();
            return;
        }

        _window = window;
        var trayIcon = LoadTrayIcon();

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = trayIcon,
            Text = DefaultTrayTooltip,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
        _notifyIcon.MouseUp += OnNotifyIconMouseUp;
        _contextMenu = BuildContextMenu();
        UpdateTrayTooltip();
    }

    public void HideToTray()
    {
        if (_window is null)
        {
            return;
        }

        _window.ShowInTaskbar = false;
        _window.Hide();
        _lifecycleService.EnterBackgroundMode();
        UpdateTrayTooltip();
    }

    public void RestoreFromTray()
    {
        if (_window is null)
        {
            return;
        }

        _lifecycleService.EnterForegroundMode();
        _window.ShowInTaskbar = true;
        _window.Visibility = Visibility.Visible;
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Show();
        _window.Activate();
        UpdateTrayTooltip();
    }

    public void RequestShutdown()
    {
        if (_window is null)
        {
            WpfApplication.Current.Shutdown();
            return;
        }

        _window.CloseForShutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifecycleService.AppModeChanged -= OnAppModeChanged;

        if (_contextMenu is not null)
        {
            _contextMenu.IsOpen = false;
            _contextMenu = null;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.MouseUp -= OnNotifyIconMouseUp;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    public void ShowAutoTrackRunCompleted(AutoTrackRunResult result)
    {
        if (_notifyIcon is null || _lifecycleService.CurrentMode != AppMode.Background)
        {
            return;
        }

        if (result.TmdbRefreshed == 0 &&
            result.EpisodesQueued == 0 &&
            result.CandidatesFound == 0 &&
            result.TorrentsAdded == 0 &&
            result.LinkedCount == 0 &&
            result.Failed == 0 &&
            result.Succeeded)
        {
            UpdateTrayTooltip();
            return;
        }

        if (!NotificationCatalog.IsEnabled(
                _settingsService.Current.Notifications,
                NotificationKind.AutoTrackRunSummary))
        {
            UpdateTrayTooltip();
            return;
        }

        var title = result.Succeeded ? "Auto-Track" : "Auto-Track (issues)";
        var icon = result.Succeeded ? WinForms.ToolTipIcon.Info : WinForms.ToolTipIcon.Warning;
        var message = string.IsNullOrWhiteSpace(result.Summary)
            ? "Auto-track cycle completed."
            : Truncate(result.Summary, 240);

        try
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, icon);
        }
        catch
        {
        }

        UpdateTrayTooltip();
    }

    private void OnAppModeChanged(object? sender, AppMode mode)
    {
        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Text = _lifecycleService.CurrentMode == AppMode.Background
            ? BuildBackgroundTooltip()
            : DefaultTrayTooltip;
    }

    private void OnNotifyIconMouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button != WinForms.MouseButtons.Right)
        {
            return;
        }

        ShowTrayMenu();
    }

    private void ShowTrayMenu()
    {
        RunOnUi(() =>
        {
            if (_window is null || _contextMenu is null)
            {
                return;
            }

            RefreshCommandStates();

            var helper = new WindowInteropHelper(_window);
            helper.EnsureHandle();
            SetForegroundWindow(helper.Handle);

            var dip = GetMousePositionInDips(_window);
            _contextMenu.Placement = PlacementMode.AbsolutePoint;
            _contextMenu.PlacementTarget = _window;
            _contextMenu.HorizontalOffset = dip.X;
            _contextMenu.VerticalOffset = dip.Y;
            _contextMenu.IsOpen = true;
        });
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(WpfControl.BackgroundProperty, "AppBrushSurfaceRaised");
        menu.SetResourceReference(WpfControl.BorderBrushProperty, "AppBrushBorder");
        menu.SetResourceReference(WpfControl.ForegroundProperty, "AppBrushText");

        var shell = TryGetShell();

        menu.Items.Add(CreateMenuItem("Run Now", PackIconLucideKind.Play, shell?.RunAutoTrackNowCommand));
        menu.Items.Add(CreateMenuItem("Open Console Log", PackIconLucideKind.Terminal, shell?.OpenConsoleCommand));
        menu.Items.Add(CreateMenuItem("Open Jellyfin", PackIconLucideKind.Clapperboard, shell?.OpenJellyfinCommand));
        menu.Items.Add(CreateMenuItem("Open qBittorrent", PackIconLucideKind.Download, shell?.OpenQbittorrentCommand));
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(CreateMenuItem("Open app", PackIconLucideKind.AppWindow, click: () => RestoreAndNavigate(null)));
        menu.Items.Add(CreateMenuItem("Open Find/Add", PackIconLucideKind.Search, click: () => RestoreAndNavigate(AppWorkspaceKind.FindAdd)));
        menu.Items.Add(CreateMenuItem("Open Cart", PackIconLucideKind.ShoppingCart, click: () => RestoreAndNavigate(AppWorkspaceKind.Torrent)));
        menu.Items.Add(CreateMenuItem("Open Settings", PackIconLucideKind.Settings, click: () => RestoreAndNavigate(AppWorkspaceKind.SystemSettings)));
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(CreateMenuItem("Exit", PackIconLucideKind.LogOut, click: RequestShutdown));

        return menu;
    }

    private void RefreshCommandStates()
    {
        var shell = TryGetShell();
        if (shell is null)
        {
            return;
        }

        shell.RunAutoTrackNowCommand.NotifyCanExecuteChanged();
        shell.OpenJellyfinCommand.NotifyCanExecuteChanged();
        shell.OpenQbittorrentCommand.NotifyCanExecuteChanged();
    }

    private MainViewModel? TryGetShell() => _window?.DataContext as MainViewModel;

    private void RestoreAndNavigate(AppWorkspaceKind? workspace)
    {
        RestoreFromTray();
        if (workspace is { } kind)
        {
            _workspaceNavigator.NavigateTo(kind);
        }
    }

    private static MenuItem CreateMenuItem(
        string header,
        PackIconLucideKind iconKind,
        ICommand? command = null,
        Action? click = null)
    {
        var item = new MenuItem { Header = header };
        if (WpfApplication.Current?.TryFindResource("AppContextMenuItemStyle") is Style menuItemStyle)
        {
            item.Style = menuItemStyle;
        }

        if (command is not null)
        {
            item.Command = command;
        }
        else if (click is not null)
        {
            item.Click += (_, _) => click();
        }

        var icon = new PackIconLucide
        {
            Kind = iconKind,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(WpfControl.ForegroundProperty, "AppBrushAccent");
        item.Icon = icon;
        return item;
    }

    private static Separator CreateSeparator()
    {
        var separator = new Separator();
        separator.SetResourceReference(WpfControl.BackgroundProperty, "AppBrushBorder");
        return separator;
    }

    private void RunOnUi(Action action)
    {
        var dispatcher = _window?.Dispatcher ?? WpfApplication.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private static WpfPoint GetMousePositionInDips(Visual visual)
    {
        var mouse = WinForms.Control.MousePosition;
        var device = new WpfPoint(mouse.X, mouse.Y);
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget is not null)
        {
            return source.CompositionTarget.TransformFromDevice.Transform(device);
        }

        var dpi = VisualTreeHelper.GetDpi(visual);
        return new WpfPoint(mouse.X / dpi.DpiScaleX, mouse.Y / dpi.DpiScaleY);
    }

    private static string BuildBackgroundTooltip() => BackgroundModeTrayTooltip;

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 3)] + "...";
    }

    private static Icon LoadTrayIcon()
    {
        var resourceUri = new Uri("pack://application:,,,/Assets/app-icon.ico", UriKind.Absolute);
        var stream = WpfApplication.GetResourceStream(resourceUri)?.Stream;
        if (stream is not null)
        {
            return new Icon(stream);
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }

        return SystemIcons.Application;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
