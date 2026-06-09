using System.Windows;

namespace media_management_app.Services;

public interface IConsoleLogWindow
{
    event EventHandler Closed;

    bool IsVisible { get; }

    WindowState WindowState { get; set; }

    void Show();

    bool Activate();
}
