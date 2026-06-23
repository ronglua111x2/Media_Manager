using System.ComponentModel;
using System.Windows;
using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class PackLinkProgressWindow : Window
{
    private readonly PackLinkProgressViewModel _viewModel;

    public PackLinkProgressWindow(PackLinkProgressViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public void AllowClose() => _viewModel.AllowClose();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.CanClose)
        {
            e.Cancel = true;
        }
    }
}
