namespace media_management_app.Models;

public sealed class JellyfinScheduledTaskInfo
{
    public string? Id { get; init; }

    public string? Key { get; init; }

    public string? Name { get; init; }

    public string? Category { get; init; }

    public string? State { get; init; }

    public bool IsRunning =>
        string.Equals(State, "Running", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(State, "Cancelling", StringComparison.OrdinalIgnoreCase);

    public bool IsLibraryRefreshRelated
    {
        get
        {
            if (string.Equals(Category, "Library", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var key = Key ?? string.Empty;
            var name = Name ?? string.Empty;
            return key.Contains("Refresh", StringComparison.OrdinalIgnoreCase) ||
                   key.Contains("RefreshLibrary", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Scan Media Library", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Refresh", StringComparison.OrdinalIgnoreCase);
        }
    }
}
