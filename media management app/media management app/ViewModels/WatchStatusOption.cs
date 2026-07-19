using media_management_app.Common;

namespace media_management_app.ViewModels;

public sealed class WatchStatusOption
{
    public UserWatchStatus Status { get; init; }

    public string Label { get; init; } = string.Empty;
}

public sealed class WatchStatusFilterOption
{
    /// <summary>Null means All (no status filter).</summary>
    public UserWatchStatus? Status { get; init; }

    public string Label { get; init; } = string.Empty;
}
