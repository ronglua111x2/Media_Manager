using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using media_management_app.Common;
using media_management_app.Models;
using SkiaSharp;

namespace media_management_app.ViewModels;

public static class StatsBandPieSeries
{
    public static ISeries[] FromBands(IEnumerable<PersonalRatingBandCount> bands)
    {
        var slices = bands.Where(band => band.Count > 0).ToList();
        if (slices.Count == 0)
        {
            return [];
        }

        var total = slices.Sum(slice => slice.Count);
        var gap = SurfaceSkColor();

        return slices
            .Select(slice => (ISeries)new PieSeries<int>
            {
                Values = [slice.Count],
                Name = slice.Label,
                Fill = new SolidColorPaint(ToSkColor(slice.Band)),
                Stroke = new SolidColorPaint(gap) { StrokeThickness = 2 },
                InnerRadius = 56,
                MaxRadialColumnWidth = 22,
                HoverPushout = 4,
                ToolTipLabelFormatter = _ =>
                {
                    var percent = 100d * slice.Count / total;
                    return $"{slice.Label}: {slice.Count} ({percent:0}%)";
                }
            })
            .ToArray();
    }

    private static SKColor ToSkColor(EpisodeRatingBand band)
    {
        if (EpisodeRatingBandCatalog.ResolveBrush(band) is SolidColorBrush solid)
        {
            var color = solid.Color;
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        var fallback = EpisodeRatingBandCatalog.Get(band).FallbackColor;
        return new SKColor(fallback.R, fallback.G, fallback.B, fallback.A);
    }

    private static SKColor SurfaceSkColor()
    {
        if (System.Windows.Application.Current?.TryFindResource("AppBrushSurfaceRaised") is SolidColorBrush brush)
        {
            var color = brush.Color;
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        return new SKColor(22, 28, 40);
    }
}
