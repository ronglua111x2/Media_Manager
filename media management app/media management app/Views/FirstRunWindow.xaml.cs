using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class FirstRunWindow : System.Windows.Window
{
    public FirstRunWindow(FirstRunViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        viewModel.QuitRequested += (_, _) => System.Windows.Application.Current?.Shutdown();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        if (DataContext is FirstRunViewModel viewModel)
        {
            await viewModel.ReleaseHeldWarpLeasesAsync();
        }
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is FirstRunViewModel viewModel)
        {
            await viewModel.InitializeCommand.ExecuteAsync(null);
        }
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (DataContext is not FirstRunViewModel viewModel)
        {
            return;
        }

        if (viewModel.SuppressClosePrompt)
        {
            return;
        }

        if (!viewModel.IsGated)
        {
            return;
        }

        e.Cancel = true;
        viewModel.RequestQuit();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.DependencyObject source &&
            IsInsideCloseButton(source))
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private bool IsInsideCloseButton(System.Windows.DependencyObject source)
    {
        System.Windows.DependencyObject? current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, CloseCaptionButton))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
