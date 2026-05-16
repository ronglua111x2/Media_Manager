using System.Windows;
using media_management_app.ViewModels;

namespace media_management_app;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
