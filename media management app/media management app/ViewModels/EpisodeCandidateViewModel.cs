using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public partial class EpisodeCandidateViewModel : ObservableObject
{
    public EpisodeCandidateViewModel(EpisodeFetchCandidate candidate)
    {
        Candidate = candidate;
    }

    public EpisodeFetchCandidate Candidate { get; }

    public string FileName => Candidate.FileName;

    public string DisplayName
    {
        get
        {
            var title = FileName.Length <= 72 ? FileName : $"{FileName[..69]}...";
            var quality = string.IsNullOrWhiteSpace(Candidate.QualityLabel) ? "unknown" : Candidate.QualityLabel;
            var audio = string.IsNullOrWhiteSpace(Candidate.AudioCodecLabel) ? string.Empty : $" | {Candidate.AudioCodecLabel}";
            var size = string.IsNullOrWhiteSpace(FileSizeDisplay) ? "unknown size" : FileSizeDisplay;
            return $"{title} | {quality}{audio} | {size} | {Seeders} seeders";
        }
    }

    public string FileSizeDisplay => Candidate.FileSizeDisplay;

    public int Seeders => Candidate.Seeders;

    public int QualityScore => Candidate.QualityScore;

    public int TotalScore => Candidate.TotalScore;

    public string PluginName => Candidate.PluginName;
}
