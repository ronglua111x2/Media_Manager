namespace media_management_app.Models;

public sealed class ShowMetadataSyncResult
{
    public long ShowId { get; init; }

    public string Title { get; init; } = string.Empty;

    public bool Success { get; init; }

    public int NewEpisodesAdded { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed class MovieMetadataSyncResult
{
    public long MovieId { get; init; }

    public string Title { get; init; } = string.Empty;

    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed class OngoingShowsSyncResult
{
    public int ShowsChecked { get; init; }

    public int ShowsSucceeded { get; init; }

    public int ShowsFailed { get; init; }

    public int TotalNewEpisodesAdded { get; init; }

    public IReadOnlyList<ShowMetadataSyncResult> ShowResults { get; init; } = [];

    public string Summary =>
        $"Checked {ShowsChecked} ongoing show(s). {ShowsSucceeded} succeeded, {ShowsFailed} failed. {TotalNewEpisodesAdded} new episode(s) added.";
}

public sealed class LibraryMetadataSyncResult
{
    public int ShowsRefreshed { get; init; }

    public int ShowsFailed { get; init; }

    public int MoviesRefreshed { get; init; }

    public int MoviesFailed { get; init; }

    public int TotalNewEpisodesAdded { get; init; }

    public IReadOnlyList<ShowMetadataSyncResult> ShowResults { get; init; } = [];

    public IReadOnlyList<MovieMetadataSyncResult> MovieResults { get; init; } = [];

    public string Summary =>
        $"{ShowsRefreshed} show(s), {MoviesRefreshed} movie(s) refreshed. {ShowsFailed + MoviesFailed} failed. {TotalNewEpisodesAdded} new episode(s) added.";
}
