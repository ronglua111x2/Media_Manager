namespace media_management_app.Models;

public sealed class GeminiModelRefreshResult
{
    public int ActiveCount { get; init; }

    public int DeprecatedCount { get; init; }

    public int NewlyMarkedDeprecatedCount { get; init; }

    public int NewlyAddedCount { get; init; }

    public int RestoredCount { get; init; }

    public int ApiModelCount { get; init; }

    public string Summary { get; init; } = string.Empty;
}
