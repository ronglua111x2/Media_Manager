using System.Globalization;
using System.Windows.Data;
using MahApps.Metro.IconPacks;
using media_management_app.Common;

namespace media_management_app.Converters;

public sealed class UserWatchStatusToLucideKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value is UserWatchStatus direct ? direct : UserWatchStatus.None;
        return status switch
        {
            UserWatchStatus.Watching => PackIconLucideKind.Play,
            UserWatchStatus.Completed => PackIconLucideKind.CircleCheck,
            UserWatchStatus.OnHold => PackIconLucideKind.Pause,
            UserWatchStatus.Dropped => PackIconLucideKind.CircleX,
            UserWatchStatus.PlanToWatch => PackIconLucideKind.Bookmark,
            _ => PackIconLucideKind.CircleDashed
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
