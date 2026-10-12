using media_management_app.Common;

namespace media_management_app.Models;

public enum MediaImportGroupStatus
{
    Ready = 0,
    NeedsReview = 1,
    Ignored = 2
}

public sealed class MediaImportCandidate
{
    public MediaKind MediaKind { get; set; }

    public int TmdbId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public double Confidence { get; set; }

    public string MatchReason { get; set; } = string.Empty;

    public string DisplayTitle => Year is null ? Title : $"{Title} ({Year})";
}

public sealed class MediaImportPreviewGroup
{
    public MediaKind MediaKind { get; set; }

    public string ParsedTitle { get; set; } = string.Empty;

    public int? ParsedYear { get; set; }

    public MediaImportGroupStatus Status { get; set; }

    public MediaImportCandidate? SelectedCandidate { get; set; }

    public List<MediaImportCandidate> Candidates { get; set; } = [];

    public List<SourceItem> Items { get; set; } = [];
}

public sealed class MediaImportPreviewResult
{
    public List<MediaImportPreviewGroup> Groups { get; set; } = [];

    public int ScannedFileCount { get; set; }

    public int ReadyFileCount { get; set; }

    public int NeedsReviewFileCount { get; set; }

    public int IgnoredFileCount { get; set; }

    public string Summary =>
        $"Scanned {ScannedFileCount} file(s). Ready={ReadyFileCount}, Review={NeedsReviewFileCount}, Ignored={IgnoredFileCount}.";
}

public sealed class MediaImportCommitGroup
{
    public MediaKind MediaKind { get; set; }

    public MediaImportCandidate SelectedCandidate { get; set; } = new();

    public IReadOnlyList<SourceItem> Items { get; set; } = [];
}

public sealed class MediaImportCommitResult
{
    public int ImportedShowCount { get; set; }

    public int ImportedMovieCount { get; set; }

    public int ImportedFileCount { get; set; }

    public int SkippedFileCount { get; set; }

    public List<(MediaKind MediaKind, long MediaId)> ImportedMedia { get; } = [];

    public string Summary =>
        $"Imported {ImportedFileCount} file(s), {ImportedShowCount} show(s), {ImportedMovieCount} movie(s). Skipped={SkippedFileCount}.";
}
