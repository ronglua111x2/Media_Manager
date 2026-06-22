namespace media_management_app.Models;

public sealed class PackFolderTreeAnalysis
{
    public IReadOnlyList<int> FolderCoveredSeasons { get; init; } = [];

    public IReadOnlyDictionary<string, int?> PathSeasonHints { get; init; } =
        new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);

    public bool HasExtrasFolder { get; init; }

    public bool HasSpecialsFolder { get; init; }

    public bool HasMoviesFolder { get; init; }
}
