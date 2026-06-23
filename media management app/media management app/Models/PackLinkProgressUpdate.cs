namespace media_management_app.Models;

public sealed class PackLinkProgressUpdate
{
    public PackLinkProgressStep Step { get; init; }

    public PackLinkProgressStatus Status { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Detail { get; init; }
}
