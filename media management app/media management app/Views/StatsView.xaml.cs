using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace media_management_app.Views;

public partial class StatsView : System.Windows.Controls.UserControl
{
    private const double JumpButtonGap = 6;

    public StatsView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateJumpButtonPlacement();
        StatsScroller.SizeChanged += (_, _) => UpdateJumpButtonPlacement();
        StatsScroller.ScrollChanged += (_, _) => UpdateJumpButtonPlacement();
    }

    private void JumpToSectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (TocPopup.IsOpen)
        {
            TocPopup.IsOpen = false;
            return;
        }

        TocList.ItemsSource = StatsToc.Collect(StatsScrollContent);
        TocPopup.IsOpen = true;
    }

    private void TocItem_Click(object sender, RoutedEventArgs e)
    {
        TocPopup.IsOpen = false;
        if (sender is not System.Windows.Controls.Button { Tag: FrameworkElement target })
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() => ScrollSectionToTop(target), DispatcherPriority.Loaded);
    }

    private void ScrollSectionToTop(FrameworkElement target)
    {
        if (StatsScroller.Content is not FrameworkElement content)
        {
            return;
        }

        var y = target.TranslatePoint(new System.Windows.Point(0, 0), content).Y;
        StatsScroller.ScrollToVerticalOffset(Math.Max(0, y));
    }

    private void UpdateJumpButtonPlacement()
    {
        var inset = JumpButtonGap;
        if (StatsScroller.ComputedVerticalScrollBarVisibility == Visibility.Visible)
        {
            inset += SystemParameters.VerticalScrollBarWidth;
        }

        JumpToSectionButton.Margin = new Thickness(0, 0, inset, JumpButtonGap);
    }
}
