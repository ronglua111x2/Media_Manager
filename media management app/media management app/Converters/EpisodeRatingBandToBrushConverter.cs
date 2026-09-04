using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using media_management_app.ViewModels;

namespace media_management_app.Converters;

public sealed class EpisodeRatingBandToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is EpisodeRatingBand band)
        {
            return EpisodeRatingBandCatalog.ResolveBrush(band);
        }

        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
