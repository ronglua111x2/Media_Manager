using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace media_management_app.ViewModels;

public sealed partial class EpisodeRatingCellViewModel : ObservableObject
{
    public const double PlotTrackHeight = 96;
    public const double RatedPillHeight = 28;
    public const double UnratedPillHeight = 20;

    public EpisodeRatingCellViewModel(int episodeNumber, double? userRating)
    {
        EpisodeNumber = episodeNumber;
        UserRating = userRating;
        Band = EpisodeRatingBandCatalog.FromRating(userRating);
        PillHeight = userRating is null ? UnratedPillHeight : RatedPillHeight;
        ColumnHeight = userRating is null
            ? UnratedPillHeight
            : ColumnHeightFor(userRating.Value);
    }

    public int EpisodeNumber { get; }

    public double? UserRating { get; }

    public bool HasRating => UserRating.HasValue;

    public string Label => $"E{EpisodeNumber}";

    public string RatingText => HasRating
        ? UserRating!.Value.ToString("0.0", CultureInfo.InvariantCulture)
        : "—";

    public string HeatmapValueText => HasRating
        ? UserRating!.Value.ToString("0.0", CultureInfo.InvariantCulture)
        : "N/A";

    public EpisodeRatingBand Band { get; }

    public double PillHeight { get; }

    public double ColumnHeight { get; }

    public static double AverageGuideOffset(double rating)
    {
        return PlotTrackHeight - ColumnHeightFor(rating);
    }

    private static double ColumnHeightFor(double rating)
    {
        var height = rating / 10.0 * PlotTrackHeight;
        return Math.Clamp(height, RatedPillHeight, PlotTrackHeight);
    }

    [ObservableProperty]
    private bool isSelected;
}
