namespace media_management_app.Models;

using media_management_app.Common;

public sealed class AutoTorrentSettings
{
    public string QbittorrentWebUiUrl { get; set; } = "http://localhost:8080";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? DownloadFolder { get; set; }

    public List<string> DownloadFolders { get; set; } = [];

    /// <summary>Legacy single category from older settings; migrated to <see cref="TvShowCategoryName"/> / <see cref="MovieCategoryName"/>.</summary>
    public string CategoryName { get; set; } = "AutoTorrent";

    public string TvShowCategoryName { get; set; } = AppConstants.QbittorrentTvShowCategory;

    public string MovieCategoryName { get; set; } = AppConstants.QbittorrentMovieCategory;

    public bool AutoLinkCompletedDownloads { get; set; }

    public int MaxCandidatesPerFetch { get; set; } = 3;

    public int MaxParallelSearches { get; set; } = 3;

    public bool UseShowSnapshotSearch { get; set; } = true;

    public int SnapshotTargetResults { get; set; } = 2000;

    public int MovieSearchTimeoutSeconds { get; set; } = 30;

    public int ParallelSearchTimeoutSeconds { get; set; } = 30;

    public int SnapshotTimeoutSeconds { get; set; } = 120;

    public int SnapshotIdleTimeoutSeconds { get; set; } = 10;

    public int LocalMatchWorkers { get; set; } = 3;

    public bool DeduplicateCandidates { get; set; } = true;

    public bool FuzzyDeduplicate { get; set; }

    public int FuzzyDeduplicateSizeToleranceMb { get; set; } = 5;

    public bool EnableCandidateMetadataProbe { get; set; }

    /// <summary>
    /// When true, closing the standalone qBittorrent viewer asks for confirmation.
    /// Skipped for background auto-close and app shutdown.
    /// </summary>
    public bool ConfirmCloseViewer { get; set; } = true;

    /// <summary>
    /// When true, the standalone qBittorrent viewer closes (no confirm) when the app
    /// enters background mode.
    /// </summary>
    public bool AutoCloseViewerOnBackground { get; set; } = true;

    /// <summary>
    /// Opt-in process recovery when WebUI is unbound but qbittorrent.exe is still running.
    /// </summary>
    public QbittorrentProcessRestartSettings ProcessRestart { get; set; } = new();

    public string GetCategoryFor(MediaKind targetKind)
    {
        return targetKind == MediaKind.Movie
            ? ResolveCategory(MovieCategoryName, AppConstants.QbittorrentMovieCategory)
            : ResolveCategory(TvShowCategoryName, AppConstants.QbittorrentTvShowCategory);
    }

    private static string ResolveCategory(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
