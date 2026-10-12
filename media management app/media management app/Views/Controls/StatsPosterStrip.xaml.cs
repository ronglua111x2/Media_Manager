using System.Windows;
using media_management_app.ViewModels;

namespace media_management_app.Views.Controls;

public partial class StatsPosterStrip : System.Windows.Controls.UserControl
{
    public StatsPosterStrip()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ReportWidth();
        Loaded += (_, _) => ReportWidth();
        DataContextChanged += (_, _) => ReportWidth();
    }

    private void ReportWidth()
    {
        if (DataContext is StatsPosterStripViewModel strip && ActualWidth > 0)
        {
            strip.SetAvailableWidth(ActualWidth);
        }
    }
}
