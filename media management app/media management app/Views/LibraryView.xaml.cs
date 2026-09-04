using System.Windows;
using System.Windows.Input;
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

    private void EpisodeRatingTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || sender is not System.Windows.Controls.TextBox box
            || box.DataContext is not LibraryEpisodeRowViewModel row
            || DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        CommitTextBoxText(box);
        viewModel.CommitEpisodeRatingCommand.Execute(row);
        e.Handled = true;
    }

    private void TitleRatingTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel)
        {
            viewModel.CommitTitleRatingCommand.Execute(null);
        }
    }

    private void TitleRatingTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        if (sender is System.Windows.Controls.TextBox box)
        {
            CommitTextBoxText(box);
        }

        viewModel.CommitTitleRatingCommand.Execute(null);
        e.Handled = true;
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
        NestedScrollViewer.OnPreviewMouseWheel(sender, e);
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

    private static void CommitTextBoxText(System.Windows.Controls.TextBox box)
    {
        box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
    }
}
