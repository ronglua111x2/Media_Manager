using System.Windows;
using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class PackLinkReviewWindow : Window
{
    public PackLinkReviewWindow(PackLinkReviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not PackLinkReviewViewModel viewModel)
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.Escape)
        {
            viewModel.CancelCommand.Execute(this);
            e.Handled = true;
        }
    }
}
