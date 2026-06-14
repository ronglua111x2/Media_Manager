using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace media_management_app.Services;

public sealed class TrayIconService : ITrayIconService
{
    private const string TrayTooltip = "Media Manager";

    private MainWindow? _window;
    private WinForms.NotifyIcon? _notifyIcon;
    private bool _disposed;

    public bool IsInitialized => _notifyIcon is not null;

    public void Initialize(MainWindow window)
    {
        if (_notifyIcon is not null)
        {
            _window = window;
            return;
        }

        _window = window;
        var trayIcon = LoadTrayIcon();

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = trayIcon,
            Text = TrayTooltip,
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
    }

    public void HideToTray()
    {
        if (_window is null)
        {
            return;
        }

        _window.ShowInTaskbar = false;
        _window.Hide();
    }

    public void RestoreFromTray()
    {
        if (_window is null)
        {
            return;
        }

        _window.ShowInTaskbar = true;
        _window.Visibility = Visibility.Visible;
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Show();
        _window.Activate();
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
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
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
