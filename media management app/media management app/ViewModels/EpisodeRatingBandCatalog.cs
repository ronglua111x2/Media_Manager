using System.Windows.Media;
using media_management_app.Common;
using media_management_app.Services;
using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.Brush;

namespace media_management_app.ViewModels;

public sealed record EpisodeRatingBandDefinition(
    EpisodeRatingBand Band,
    double? MinInclusive,
    string Label,
    string? ResourceKey,
    MediaColor FallbackColor);

public static class EpisodeRatingBandCatalog
{
    public static IReadOnlyList<EpisodeRatingBandDefinition> All { get; } =
    [
        new(EpisodeRatingBand.Legendary, 9.5, "Legendary", "AppBrushShowAccent", MediaColor.FromRgb(46, 196, 191)),
        new(EpisodeRatingBand.Masterpiece, 9.0, "Masterpiece", "AppBrushWatchCompleted", MediaColor.FromRgb(61, 206, 122)),
        new(EpisodeRatingBand.Great, 8.0, "Great", null, MediaColor.FromRgb(95, 209, 138)),
        new(EpisodeRatingBand.Good, 7.0, "Good", "AppBrushWarning", MediaColor.FromRgb(217, 154, 58)),
        new(EpisodeRatingBand.Average, 6.0, "Fair", null, MediaColor.FromRgb(232, 196, 74)),
        new(EpisodeRatingBand.Weak, 0.0, "Weak", "AppBrushDanger", MediaColor.FromRgb(224, 85, 85)),
        new(EpisodeRatingBand.Unrated, null, "N/A", "AppBrushChipMuted", MediaColor.FromRgb(37, 45, 61))
    ];

    public static IReadOnlyList<EpisodeRatingBandDefinition> LegendEntries { get; } = All;

    public static EpisodeRatingBandDefinition Get(EpisodeRatingBand band) =>
        All.First(entry => entry.Band == band);

    public static EpisodeRatingBand FromRating(double? rating) =>
        EpisodeRatingBandRules.FromRating(rating);

    public static MediaBrush ResolveBrush(EpisodeRatingBand band)
    {
        var entry = Get(band);
        if (!string.IsNullOrWhiteSpace(entry.ResourceKey)
            && System.Windows.Application.Current?.TryFindResource(entry.ResourceKey) is MediaBrush themed)
        {
            return themed;
        }

        return new SolidColorBrush(entry.FallbackColor);
    }
}
