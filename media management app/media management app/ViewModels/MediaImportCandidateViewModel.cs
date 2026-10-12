using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed class MediaImportCandidateViewModel
{
    public MediaImportCandidateViewModel(MediaImportGroupViewModel group, MediaImportCandidate candidate)
    {
        Group = group;
        Candidate = candidate;
    }

    public MediaImportGroupViewModel Group { get; }

    public MediaImportCandidate Candidate { get; }

    public string DisplayTitle => Candidate.DisplayTitle;

    public string ConfidenceLabel => $"{Candidate.Confidence:0}%";

    public string MatchReason => Candidate.MatchReason;
}
