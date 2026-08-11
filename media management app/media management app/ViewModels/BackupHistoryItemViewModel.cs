namespace media_management_app.ViewModels;

public sealed class BackupHistoryItemViewModel
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public DateTime? CreatedTimeUtc { get; init; }

    public string DisplayLabel => CreatedTimeUtc is { } createdUtc
        ? $"{createdUtc.ToLocalTime():g} ({Name})"
        : Name;
}
