using System.Windows;
using media_management_app.ViewModels;

namespace media_management_app.Views.Controls;

/// <summary>Pill with the native search quota of every site; click opens the per-site table.</summary>
public partial class SearchQuotaPill : System.Windows.Controls.UserControl
{
    public SearchQuotaPill()
    {
        InitializeComponent();
    }

    private void OnPillClick(object sender, RoutedEventArgs e)
    {
        if (!QuotaPopup.IsOpen && DataContext is SearchQuotaViewModel quota)
        {
            quota.Refresh();
        }

        QuotaPopup.IsOpen = !QuotaPopup.IsOpen;
    }
}
