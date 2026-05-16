using media_management_app.Common;

namespace media_management_app.Models;

public sealed class SeriesMapping
{
    public long Id { get; set; }

    public string ParsedTitle { get; set; } = string.Empty;

    public ParserPattern ParserPattern { get; set; }

    public string MatchedTitle { get; set; } = string.Empty;

    public int? MatchedYear { get; set; }

    public string Provider { get; set; } = "tmdb";

    public string ProviderId { get; set; } = string.Empty;

    public bool UseAbsoluteAnimeMapping { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
