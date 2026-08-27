using System.Globalization;
using System.Windows.Data;

namespace media_management_app.Converters;

public sealed class ResourceKeyToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (!string.IsNullOrWhiteSpace(key) &&
            System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.Brush brush)
        {
            return brush;
        }

        return System.Windows.Application.Current?.TryFindResource("AppBrushText") as System.Windows.Media.Brush
               ?? System.Windows.Media.Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
