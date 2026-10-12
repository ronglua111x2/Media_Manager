using media_management_app.Views;
using WpfApplication = System.Windows.Application;

namespace media_management_app.Services;

public sealed class ConsoleWindowService : IConsoleWindowService
{
    private readonly Func<IConsoleLogWindow> _windowFactory;
    private readonly ISettingsService? _settingsService;
    private IConsoleLogWindow? _window;

    public ConsoleWindowService(
        IAppLogger logger,
        IAppLifecycleService lifecycleService,
        ISettingsService settingsService)
        : this(() => new ConsoleLogWindow(logger), lifecycleService, settingsService)
    {
    }

    public ConsoleWindowService(Func<IConsoleLogWindow> windowFactory)
        : this(windowFactory, lifecycleService: null, settingsService: null)
    {
    }

    public ConsoleWindowService(
        Func<IConsoleLogWindow> windowFactory,
        IAppLifecycleService? lifecycleService,
        ISettingsService? settingsService)
    {
        _windowFactory = windowFactory;
        _settingsService = settingsService;
        if (lifecycleService is not null)
        {
            lifecycleService.AppModeChanged += OnAppModeChanged;
        }
    }

    public void ShowConsole()
    {
        if (_window is null)
        {
            _window = _windowFactory();
            _window.Closed += (_, _) => _window = null;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == System.Windows.WindowState.Minimized)
        {
            _window.WindowState = System.Windows.WindowState.Normal;
        }

        _window.Activate();
    }

    private void OnAppModeChanged(object? sender, AppMode mode)
    {
        if (mode != AppMode.Background)
        {
            return;
        }

        if (_settingsService?.Current.Logs?.AutoCloseConsoleOnBackground != true)
        {
            return;
        }

        RunOnUi(CloseConsole);
    }

    private void CloseConsole()
    {
        _window?.CloseForShutdown();
    }

    private void RunOnUi(Action action)
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
