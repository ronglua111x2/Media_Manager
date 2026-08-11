using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void ThoughtEditTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        viewModel.CommitThoughtCommand.Execute(null);
        e.Handled = true;
    }
}
