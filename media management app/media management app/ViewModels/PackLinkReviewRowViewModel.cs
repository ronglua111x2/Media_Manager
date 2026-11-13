namespace media_management_app.ViewModels;

public sealed class PackLinkReviewRowViewModel
{
    public string FileName { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    /// <summary><c>S00E09</c>, "Not mapped (orphan extras folder)" or "Skipped".</summary>
    public string TargetLabel { get; init; } = string.Empty;

    /// <summary>TMDB title of the mapped episode, so a wrong pair can be spotted without opening TMDB.</summary>
    public string TmdbTitle { get; init; } = string.Empty;

    /// <summary>TMDB air date of the mapped episode (yyyy-MM-dd), empty when unknown.</summary>
    public string AirDateLabel { get; init; } = string.Empty;

    /// <summary>Where the pair comes from: S00Exx in name, season window, inferred, title, OVA number, Gemini...</summary>
    public string SourceLabel { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public bool IsMatched { get; init; }

    public bool IsSkipped { get; init; }

    /// <summary>True for every row the user should look at: inferred, unmapped or skipped.</summary>
    public bool NeedsAttention { get; init; }
}
