using media_management_app.Models;

namespace media_management_app.Services;

public static class HeatmapStripLayout
{
    public const double StoryCellWidth = 56;
    public const double StoryCellHeight = 52;
    public const double ExtraCellWidth = 48;
    public const double ExtraCellHeight = 44;
    public const double CellMargin = 6;

    public const double StorySlotWidth = StoryCellWidth + CellMargin;
    public const double ExtraSlotWidth = ExtraCellWidth + CellMargin;

    public const int VisualBatchSize = 48;

    public const double PosterSlotWidth = 102;

    public const int PosterCollapsedRows = 3;

    public const int PosterVisualBatchSize = 8;

    public static int FitCount(double availableWidth, double slotWidth)
    {
        if (availableWidth <= 0 || slotWidth <= 0)
        {
            return 0;
        }

        return (int)Math.Floor(availableWidth / slotWidth);
    }

    public static bool NeedsExpand(int seasonGroupCount, int previewCellCount, int fitCount) =>
        seasonGroupCount > 1 || previewCellCount > fitCount;

    public static int NextBatchCount(int remaining, int batchSize = VisualBatchSize)
    {
        if (remaining <= 0 || batchSize <= 0)
        {
            return 0;
        }

        return Math.Min(remaining, batchSize);
    }

    public static int PosterFitCount(double availableWidth)
    {
        var perRow = FitCount(availableWidth, PosterSlotWidth);
        if (perRow <= 0)
        {
            return 0;
        }

        return perRow * PosterCollapsedRows;
    }

    public static string TitleStripFingerprint(IEnumerable<TitleRatingCard> cards)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var card in cards)
        {
            builder.Append((int)card.MediaKind)
                .Append(':')
                .Append(card.MediaId)
                .Append(':')
                .Append(card.Rating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                .Append(';');
        }

        return builder.ToString();
    }

    public static string Fingerprint(IEnumerable<ShowHeatmapRow> rows)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var row in rows)
        {
            builder.Append(row.ShowId)
                .Append(':')
                .Append(row.EpisodeCount)
                .Append(':')
                .Append(row.RatedEpisodeCount);
            AppendSeasons(builder, row.Seasons);
            AppendSeasons(builder, row.ExtraSeasons);
            builder.Append(';');
        }

        return builder.ToString();
    }

    private static void AppendSeasons(System.Text.StringBuilder builder, IReadOnlyList<HeatmapSeasonGroup> seasons)
    {
        foreach (var season in seasons)
        {
            builder.Append('|')
                .Append(season.SeasonNumber)
                .Append(',')
                .Append(season.Cells.Count);
        }
    }
}
