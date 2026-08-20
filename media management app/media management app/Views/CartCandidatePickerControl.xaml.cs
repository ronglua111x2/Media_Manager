using media_management_app.ViewModels;

namespace media_management_app.Views;

public partial class CartCandidatePickerControl : System.Windows.Controls.UserControl
{
    public CartCandidatePickerControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ClosePopup();
    }

    private void CandidateListBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is not TorrentOrderViewModel ||
            sender is not System.Windows.Controls.ListBox)
        {
            return;
        }

        ClosePopup();
    }

    private void BlacklistButton_OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button { Command: { } command } button &&
            command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
        }
    }

    private void ClosePopup()
    {
        PickerToggle.IsChecked = false;
    }
}
