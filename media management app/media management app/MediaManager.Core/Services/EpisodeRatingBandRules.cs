using media_management_app.Common;

namespace media_management_app.Services;

public static class EpisodeRatingBandRules
{
    public static IReadOnlyList<(EpisodeRatingBand Band, double MinInclusive, string Label)> RatedBands { get; } =
    [
        (EpisodeRatingBand.Legendary, 9.5, "Legendary"),
        (EpisodeRatingBand.Masterpiece, 9.0, "Masterpiece"),
        (EpisodeRatingBand.Great, 8.0, "Great"),
        (EpisodeRatingBand.Good, 7.0, "Good"),
        (EpisodeRatingBand.Average, 6.0, "Fair"),
        (EpisodeRatingBand.Weak, 0.0, "Weak")
    ];

    public static string Label(EpisodeRatingBand band) => band switch
    {
        EpisodeRatingBand.Legendary => "Legendary",
        EpisodeRatingBand.Masterpiece => "Masterpiece",
        EpisodeRatingBand.Great => "Great",
        EpisodeRatingBand.Good => "Good",
        EpisodeRatingBand.Average => "Fair",
        EpisodeRatingBand.Weak => "Weak",
        _ => "N/A"
    };

    public static EpisodeRatingBand FromRating(double? rating)
    {
        if (rating is null)
        {
            return EpisodeRatingBand.Unrated;
        }

        foreach (var (band, min, _) in RatedBands)
        {
            if (rating.Value >= min)
            {
                return band;
            }
        }

        return EpisodeRatingBand.Unrated;
    }
}
