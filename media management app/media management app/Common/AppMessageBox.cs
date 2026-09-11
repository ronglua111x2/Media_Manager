using System.Windows.Threading;
using media_management_app.Views;
using WpfApplication = System.Windows.Application;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfWindow = System.Windows.Window;
using WpfWindowStartupLocation = System.Windows.WindowStartupLocation;

namespace media_management_app.Common;

public static class AppMessageBox
{
    public static WpfMessageBoxResult Show(
        string message,
        string caption,
        WpfMessageBoxButton buttons,
        WpfMessageBoxImage image)
        => Show(owner: null, message, caption, buttons, image, WpfMessageBoxResult.None);

    public static WpfMessageBoxResult Show(
        WpfWindow? owner,
        string message,
        string caption,
        WpfMessageBoxButton buttons,
        WpfMessageBoxImage image)
        => Show(owner, message, caption, buttons, image, WpfMessageBoxResult.None);

    public static WpfMessageBoxResult Show(
        string message,
        string caption,
        WpfMessageBoxButton buttons,
        WpfMessageBoxImage image,
        WpfMessageBoxResult defaultResult)
        => Show(owner: null, message, caption, buttons, image, defaultResult);

    public static WpfMessageBoxResult Show(
        WpfWindow? owner,
        string message,
        string caption,
        WpfMessageBoxButton buttons,
        WpfMessageBoxImage image,
        WpfMessageBoxResult defaultResult)
    {
        var dispatcher = WpfApplication.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (!dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(() =>
                ShowCore(owner, message, caption, buttons, image, defaultResult));
        }

        return ShowCore(owner, message, caption, buttons, image, defaultResult);
    }

    private static WpfMessageBoxResult ShowCore(
        WpfWindow? owner,
        string message,
        string caption,
        WpfMessageBoxButton buttons,
        WpfMessageBoxImage image,
        WpfMessageBoxResult defaultResult)
    {
        var window = new AppMessageBoxWindow();
        window.Configure(message, caption, buttons, image, defaultResult);

        var resolvedOwner = ResolveOwner(owner);
        if (resolvedOwner is not null)
        {
            window.Owner = resolvedOwner;
            window.WindowStartupLocation = WpfWindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WpfWindowStartupLocation.CenterScreen;
        }

        window.ShowDialog();
        return window.Result;
    }

    private static WpfWindow? ResolveOwner(WpfWindow? requested)
    {
        if (IsUsableOwner(requested))
        {
            return requested;
        }

        var main = WpfApplication.Current?.MainWindow;
        return IsUsableOwner(main) ? main : null;
    }

    private static bool IsUsableOwner(WpfWindow? window) =>
        window is { IsLoaded: true, IsVisible: true };
}
