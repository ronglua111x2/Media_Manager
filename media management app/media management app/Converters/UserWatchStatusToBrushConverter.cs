using System.Globalization;
using System.Windows.Data;
using media_management_app.Common;

namespace media_management_app.Converters;

public sealed class UserWatchStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value is UserWatchStatus direct ? direct : UserWatchStatus.None;
        var key = GetBrushKey(status);
        return System.Windows.Application.Current?.TryFindResource(key) as System.Windows.Media.Brush
               ?? System.Windows.Application.Current?.TryFindResource("AppBrushMutedText") as System.Windows.Media.Brush
               ?? System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;

    public static string GetBrushKey(UserWatchStatus status) => status switch
    {
        UserWatchStatus.Watching => "AppBrushWatchWatching",
        UserWatchStatus.Completed => "AppBrushWatchCompleted",
        UserWatchStatus.OnHold => "AppBrushWatchOnHold",
        UserWatchStatus.Dropped => "AppBrushWatchDropped",
        UserWatchStatus.PlanToWatch => "AppBrushWatchPlanToWatch",
        _ => "AppBrushWatchUnset"
    };
}
