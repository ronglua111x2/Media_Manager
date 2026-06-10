using System.Windows;
using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class QbittorrentWorkspaceView : System.Windows.Controls.UserControl
{
    public QbittorrentWorkspaceView()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not QbittorrentWorkspaceViewModel viewModel)
        {
            return;
        }

        await viewModel.AttachBrowserAsync(BrowserHost);
    }
}
