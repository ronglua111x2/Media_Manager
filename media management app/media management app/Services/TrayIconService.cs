using System.Drawing;
using System.Windows;
using media_management_app.Models;
using WinForms = System.Windows.Forms;

namespace media_management_app.Services;

public sealed class TrayIconService : ITrayIconService
{
    private const string AppName = "Media Manager";
    private const string DefaultTrayTooltip = AppName;
    private const string BackgroundModeTrayTooltip = $"{AppName} - Background Mode";

    private readonly IAppLifecycleService _lifecycleService;

    private MainWindow? _window;
    private WinForms.NotifyIcon? _notifyIcon;
    private bool _disposed;

    public TrayIconService(IAppLifecycleService lifecycleService)
    {
        _lifecycleService = lifecycleService;
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

        var contextMenu = new WinForms.ContextMenuStrip();
        var openItem = new WinForms.ToolStripMenuItem("Open");
        openItem.Click += (_, _) => RestoreFromTray();
        contextMenu.Items.Add(openItem);

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => RequestShutdown();
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
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
            System.Windows.Application.Current.Shutdown();
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

        if (_notifyIcon is not null)
        {
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
        var stream = System.Windows.Application.GetResourceStream(resourceUri)?.Stream;
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
}
