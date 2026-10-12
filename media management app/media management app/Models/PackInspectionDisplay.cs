namespace media_management_app.Models;

public sealed class PackInspectionDisplay
{
    public string CoversLine { get; init; } = string.Empty;

    public string ExtrasLine { get; init; } = string.Empty;

    public string MoviesLine { get; init; } = string.Empty;

    public bool HasExtrasLine => !string.IsNullOrWhiteSpace(ExtrasLine);

    public bool HasMoviesLine => !string.IsNullOrWhiteSpace(MoviesLine);

    public string ToStatusDetail() =>
        string.Join('\n', new[] { CoversLine, ExtrasLine, MoviesLine }.Where(line => !string.IsNullOrWhiteSpace(line)));

    public static PackInspectionDisplay ParseStatusDetail(string? statusDetail)
    {
        if (string.IsNullOrWhiteSpace(statusDetail))
        {
            return new PackInspectionDisplay();
        }

        var lines = statusDetail.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new PackInspectionDisplay
        {
            CoversLine = lines.Length > 0 ? lines[0] : string.Empty,
            ExtrasLine = lines.Length > 1 ? lines[1] : string.Empty,
            MoviesLine = lines.Length > 2 ? lines[2] : string.Empty
        };
    }
}
