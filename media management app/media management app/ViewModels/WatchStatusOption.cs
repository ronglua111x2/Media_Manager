using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;

namespace media_management_app.ViewModels;

public sealed class MediaCardSortFieldOption
{
    public MediaCardSortField Field { get; init; }

    public string Label { get; init; } = string.Empty;

    public static IReadOnlyList<MediaCardSortFieldOption> All { get; } =
    [
        new() { Field = MediaCardSortField.DateAdded, Label = "Date added" },
        new() { Field = MediaCardSortField.Type, Label = "Type" },
        new() { Field = MediaCardSortField.Title, Label = "Title" },
        new() { Field = MediaCardSortField.Rating, Label = "Rating" }
    ];
}

public sealed class WatchStatusOption
{
    public UserWatchStatus Status { get; init; }

    public string Label { get; init; } = string.Empty;
}

public sealed partial class WatchStatusFilterOption : ObservableObject
{
    public UserWatchStatus Status { get; init; }

    public string Label { get; init; } = string.Empty;

    [ObservableProperty]
    private bool isSelected;

    public static IReadOnlyList<WatchStatusFilterOption> CreateAll() =>
    [
        new() { Status = UserWatchStatus.None, Label = "Unset" },
        new() { Status = UserWatchStatus.Watching, Label = "Watching" },
        new() { Status = UserWatchStatus.Completed, Label = "Completed" },
        new() { Status = UserWatchStatus.OnHold, Label = "On-Hold" },
        new() { Status = UserWatchStatus.Dropped, Label = "Dropped" },
        new() { Status = UserWatchStatus.PlanToWatch, Label = "Plan to Watch" }
    ];

    public static void ApplySaved(
        IEnumerable<WatchStatusFilterOption> options,
        IReadOnlyList<UserWatchStatus>? saved,
        UserWatchStatus? legacy)
    {
        HashSet<UserWatchStatus> selected;
        if (saved is not null)
        {
            selected = saved.ToHashSet();
        }
        else if (legacy is { } one)
        {
            selected = [one];
        }
        else
        {
            selected = [];
        }

        foreach (var option in options)
        {
            option.IsSelected = selected.Contains(option.Status);
        }
    }

    public static void ClearAll(IEnumerable<WatchStatusFilterOption> options)
    {
        foreach (var option in options)
        {
            option.IsSelected = false;
        }
    }

    public static bool HasSelection(IEnumerable<WatchStatusFilterOption> options) =>
        options.Any(option => option.IsSelected);

    public static List<UserWatchStatus> GetSelectedStatuses(IEnumerable<WatchStatusFilterOption> options) =>
        options.Where(option => option.IsSelected).Select(option => option.Status).ToList();

    public static string GetSummaryLabel(IEnumerable<WatchStatusFilterOption> options)
    {
        var labels = options.Where(option => option.IsSelected).Select(option => option.Label).ToList();
        return labels.Count == 0 ? "All statuses" : string.Join(", ", labels);
    }

    public static bool Matches(IEnumerable<WatchStatusFilterOption> options, UserWatchStatus status)
    {
        var selected = GetSelectedStatuses(options);
        return selected.Count == 0 || selected.Contains(status);
    }
}
