using media_management_app.Common;

namespace media_management_app.Models;

public sealed class TvSeriesMatchRequest
{
    public string ShowTitle { get; init; } = string.Empty;

    public string? FileName { get; init; }

    public string? FilePath { get; init; }

    public string? ParentFolder { get; init; }

    public string? ScanText { get; init; }

    public ParserPattern ParserPattern { get; init; } = ParserPattern.Unknown;

    public int? SeasonNumber { get; init; }

    public int? EpisodeNumber { get; init; }

    public static TvSeriesMatchRequest FromSourceItem(SourceItem item)
    {
        return new TvSeriesMatchRequest
        {
            ShowTitle = item.ShowTitle ?? string.Empty,
            FileName = item.FileName,
            FilePath = item.FilePath,
            ParentFolder = item.ParentFolder,
            ScanText = item.ScanText,
            ParserPattern = item.ParserPattern,
            SeasonNumber = item.SeasonNumber,
            EpisodeNumber = item.EpisodeNumber
        };
    }
}
