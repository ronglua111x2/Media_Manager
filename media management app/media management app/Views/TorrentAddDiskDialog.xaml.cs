using System.Windows;
using System.Windows.Input;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class TorrentAddDiskDialog : Window
{
    public TorrentAddDiskDialog(
        TorrentAddDiskPlan plan,
        ITorrentAddDiskAssignmentService assignmentService,
        IDownloadFolderCatalogService downloadFolderCatalogService)
    {
        InitializeComponent();
        DataContext = new TorrentAddDiskDialogViewModel(plan, assignmentService, downloadFolderCatalogService);
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not TorrentAddDiskDialogViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            viewModel.CancelCommand.Execute(this);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && viewModel.CanConfirm)
        {
            viewModel.ConfirmCommand.Execute(this);
            e.Handled = true;
        }
    }
}
