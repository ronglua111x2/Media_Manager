using System.Windows.Controls;
using System.Windows.Input;

namespace media_management_app.Views;

public partial class StatsView : System.Windows.Controls.UserControl
{
    public StatsView()
    {
        InitializeComponent();
    }

    private void HeatmapRowScroller_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        NestedScrollViewer.OnPreviewMouseWheel(sender, e);
}
