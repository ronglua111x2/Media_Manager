namespace media_management_app.Models;

public sealed class AutoTorrentSettings
{
    public string QbittorrentWebUiUrl { get; set; } = "http://localhost:8080";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? DownloadFolder { get; set; }

    public List<string> DownloadFolders { get; set; } = [];

    public string CategoryName { get; set; } = "AutoTorrent";

    public bool AutoLinkCompletedDownloads { get; set; }

    public int MaxCandidatesPerFetch { get; set; } = 3;

    public int MaxParallelSearches { get; set; } = 3;

    public bool UseShowSnapshotSearch { get; set; } = true;

    public int SnapshotTargetResults { get; set; } = 2000;

    public int SnapshotTimeoutSeconds { get; set; } = 120;

    public int SnapshotIdleTimeoutSeconds { get; set; } = 10;

    public int LocalMatchWorkers { get; set; } = 3;

    public bool DeduplicateCandidates { get; set; } = true;

    public bool FuzzyDeduplicate { get; set; }

    public int FuzzyDeduplicateSizeToleranceMb { get; set; } = 5;

    public bool EnableCandidateMetadataProbe { get; set; }
}
