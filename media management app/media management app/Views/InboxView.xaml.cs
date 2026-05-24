using System.Windows.Controls;
using media_management_app.Models;
using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class InboxView : System.Windows.Controls.UserControl
{
    public InboxView()
    {
        InitializeComponent();
    }

    private void InboxGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid { DataContext: InboxViewModel inboxViewModel } dataGrid)
        {
            return;
        }

        inboxViewModel.UpdateSelectedItems(dataGrid.SelectedItems.OfType<SourceItem>());
    }
}
