using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class MediaImportGroupViewModel : ObservableObject
{
    public MediaImportGroupViewModel(MediaImportPreviewGroup group)
    {
        MediaKind = group.MediaKind;
        ParsedTitle = group.ParsedTitle;
        ParsedYear = group.ParsedYear;
        Status = group.Status;
        SelectedCandidate = group.SelectedCandidate;
        ManualSearchQuery = group.SelectedCandidate?.Title ?? group.ParsedTitle;

        foreach (var item in group.Items)
        {
            Files.Add(new MediaImportFileViewModel(item));
        }

        ReplaceCandidates(group.Candidates);
    }

    public MediaKind MediaKind { get; }

    public string ParsedTitle { get; }

    public int? ParsedYear { get; }

    public ObservableCollection<MediaImportFileViewModel> Files { get; } = [];

    public ObservableCollection<MediaImportCandidateViewModel> Candidates { get; } = [];

    [ObservableProperty]
    private bool isIncluded = true;

    [ObservableProperty]
    private MediaImportGroupStatus status;

    [ObservableProperty]
    private MediaImportCandidate? selectedCandidate;

    [ObservableProperty]
    private string manualSearchQuery = string.Empty;

    public bool RequiresReview => Status == MediaImportGroupStatus.NeedsReview;

    public bool IsIgnored => Status == MediaImportGroupStatus.Ignored;

    public bool CanImport => IsIncluded &&
                             !IsIgnored &&
                             SelectedCandidate is not null &&
                             Files.Any(file => file.IsIncluded);

    public string KindLabel => MediaKind == MediaKind.Movie ? "Movie" : MediaKind == MediaKind.TvEpisode ? "Show" : "Unknown";

    public string Title => SelectedCandidate?.DisplayTitle ??
                           (ParsedYear is null ? ParsedTitle : $"{ParsedTitle} ({ParsedYear})");

    public string StatusLabel => Status switch
    {
        MediaImportGroupStatus.Ready => "Ready",
        MediaImportGroupStatus.NeedsReview => "Needs review",
        MediaImportGroupStatus.Ignored => "Ignored",
        _ => string.Empty
    };

    public string FileCountLabel => Files.Count == 1 ? "1 file" : $"{Files.Count} files";

    public string ConfidenceLabel => SelectedCandidate is null ? string.Empty : $"{SelectedCandidate.Confidence:0}%";

    public string MatchReason => SelectedCandidate?.MatchReason ?? Files.FirstOrDefault()?.Notes ?? string.Empty;

    public string SearchButtonLabel => MediaKind == MediaKind.Movie ? "Search Movies" : "Search Shows";

    public void ApplyCandidate(MediaImportCandidate candidate)
    {
        SelectedCandidate = candidate;
        Status = MediaImportGroupStatus.Ready;
        foreach (var file in Files)
        {
            file.SourceItem.MatchedTitle = candidate.Title;
            file.SourceItem.MatchedYear = candidate.Year;
            file.SourceItem.Provider = "tmdb";
            file.SourceItem.ProviderId = candidate.TmdbId.ToString();
            file.SourceItem.MatchConfidence = candidate.Confidence;
            file.SourceItem.MatchReason = candidate.MatchReason;
            file.SourceItem.RequiresManualReview = false;
            file.SourceItem.Notes = null;
        }

        OnPropertyChanged(nameof(RequiresReview));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(ConfidenceLabel));
        OnPropertyChanged(nameof(MatchReason));
    }

    public void ReplaceCandidates(IEnumerable<MediaImportCandidate> candidates)
    {
        Candidates.Clear();
        foreach (var candidate in candidates)
        {
            Candidates.Add(new MediaImportCandidateViewModel(this, candidate));
        }
    }

    partial void OnIsIncludedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanImport));
    }

    partial void OnStatusChanged(MediaImportGroupStatus value)
    {
        OnPropertyChanged(nameof(RequiresReview));
        OnPropertyChanged(nameof(IsIgnored));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(StatusLabel));
    }

    partial void OnSelectedCandidateChanged(MediaImportCandidate? value)
    {
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ConfidenceLabel));
        OnPropertyChanged(nameof(MatchReason));
    }
}
