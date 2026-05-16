namespace media_management_app.ViewModels.Filters;

public sealed class FilterOption<T>
    where T : struct
{
    public string Label { get; init; } = string.Empty;

    public T? Value { get; init; }
}
