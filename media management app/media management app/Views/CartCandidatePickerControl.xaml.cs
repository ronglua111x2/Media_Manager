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
            sender is not System.Windows.Controls.ListBox ||
            e.AddedItems.Count == 0)
        {
            return;
        }

        ClosePopup();
    }

    /// <summary>
    /// Quick filter over many encodes (native-search P5): every typed word must appear in the candidate's
    /// name or attributes. The currently selected candidate always stays visible, so filtering never
    /// changes the selection.
    /// </summary>
    private void CandidateFilterBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(CandidateListBox.ItemsSource);
        if (view is null)
        {
            return;
        }

        var words = CandidateFilterBox.Text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        view.Filter = words.Length == 0
            ? null
            : item => item is TorrentOrderCandidateViewModel candidate &&
                      (ReferenceEquals(candidate, CandidateListBox.SelectedItem) ||
                       words.All(word => candidate.FilterText.Contains(word, StringComparison.OrdinalIgnoreCase)));
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
        if (CandidateFilterBox is not null && CandidateFilterBox.Text.Length > 0)
        {
            CandidateFilterBox.Text = string.Empty;
        }
    }
}
