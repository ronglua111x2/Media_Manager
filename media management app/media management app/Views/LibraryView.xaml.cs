using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class LibraryView : System.Windows.Controls.UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    private void ThoughtDisplayTextBlock_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        if (viewModel.BeginEditThoughtCommand.CanExecute(null))
        {
            viewModel.BeginEditThoughtCommand.Execute(null);
            Dispatcher.BeginInvoke(() =>
            {
                ThoughtEditTextBox.Focus();
                ThoughtEditTextBox.CaretIndex = ThoughtEditTextBox.Text?.Length ?? 0;
            });
        }
    }

    private void ThoughtEditTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel)
        {
            viewModel.CommitThoughtCommand.Execute(null);
        }
    }

    private void EpisodeRatingTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox box
            && box.DataContext is LibraryEpisodeRowViewModel row
            && DataContext is LibraryViewModel viewModel)
        {
            viewModel.CommitEpisodeRatingCommand.Execute(row);
        }
    }

    private void EpisodeThoughtTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox box
            && box.DataContext is LibraryEpisodeRowViewModel row
            && DataContext is LibraryViewModel viewModel)
        {
            viewModel.CommitEpisodeThoughtCommand.Execute(row);
        }
    }

    private void EpisodeThoughtTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || sender is not System.Windows.Controls.TextBox box
            || box.DataContext is not LibraryEpisodeRowViewModel row
            || DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        viewModel.CommitEpisodeThoughtCommand.Execute(row);
        e.Handled = true;
    }

    private void ThoughtEditTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        viewModel.CommitThoughtCommand.Execute(null);
        e.Handled = true;
    }

    private void WatchStatusComboBox_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!WatchStatusComboBox.IsVisible)
        {
            return;
        }

        SyncWatchStatusComboBoxToViewModel();
        Dispatcher.BeginInvoke(
            () => SyncWatchStatusComboBoxToViewModel(),
            DispatcherPriority.Loaded);
    }

    private void SyncWatchStatusComboBoxToViewModel()
    {
        if (DataContext is not LibraryViewModel vm)
        {
            return;
        }

        var targetIndex = -1;
        for (var i = 0; i < vm.WatchStatusOptions.Count; i++)
        {
            if (vm.WatchStatusOptions[i].Status == vm.SelectedWatchStatus)
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex >= 0 && WatchStatusComboBox.SelectedIndex != targetIndex)
        {
            WatchStatusComboBox.SelectedIndex = targetIndex;
        }
    }

    private void HorizontalScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scroller)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }

        var parent = FindAncestorScrollViewer(scroller);
        if (parent is null)
        {
            return;
        }

        parent.ScrollToVerticalOffset(parent.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject current)
    {
        var parent = VisualTreeHelper.GetParent(current);
        while (parent is not null)
        {
            if (parent is ScrollViewer viewer)
            {
                return viewer;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private void ImdbChartScrollForward_OnClick(object sender, RoutedEventArgs e)
    {
        ImdbChartScroller.ScrollToHorizontalOffset(ImdbChartScroller.HorizontalOffset + 200);
    }

    private void ImdbRatingBox_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element
            && element.DataContext is EpisodeRatingCellViewModel cell
            && DataContext is LibraryViewModel viewModel)
        {
            viewModel.SelectRatingCellCommand.Execute(cell);
        }
    }
}
