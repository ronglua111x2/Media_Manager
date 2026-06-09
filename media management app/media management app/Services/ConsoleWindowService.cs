using media_management_app.Views;

namespace media_management_app.Services;

public sealed class ConsoleWindowService : IConsoleWindowService
{
    private readonly Func<IConsoleLogWindow> _windowFactory;
    private IConsoleLogWindow? _window;

    public ConsoleWindowService(IAppLogger logger)
        : this(() => new ConsoleLogWindow(logger))
    {
    }

    public ConsoleWindowService(Func<IConsoleLogWindow> windowFactory)
    {
        _windowFactory = windowFactory;
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
}
