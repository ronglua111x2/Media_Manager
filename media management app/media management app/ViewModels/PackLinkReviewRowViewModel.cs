namespace media_management_app.ViewModels;

public sealed class PackLinkReviewRowViewModel
{
    public string FileName { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string TargetLabel { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public bool IsMatched { get; init; }
}
