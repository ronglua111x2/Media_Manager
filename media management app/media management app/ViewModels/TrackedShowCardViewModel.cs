using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public partial class TrackedShowCardViewModel : ObservableObject
{
    private readonly Action<long, string, string, int> _savePreferences;
    private static readonly IReadOnlyList<string> AvailableQualityOptions = ["2160p", "1080p", "720p", "480p"];

    public TrackedShowCardViewModel(
        TrackedShow show,
        IEnumerable<TrackedSeasonViewModel> seasons,
        Action<long, string, string, int> savePreferences)
    {
        _savePreferences = savePreferences;
        Show = show;
        Seasons = new ObservableCollection<TrackedSeasonViewModel>(seasons);
        foreach (var season in Seasons)
        {
            season.PropertyChanged += Season_PropertyChanged;
        }
        QualityOptions = new ObservableCollection<QualityOptionViewModel>(
            AvailableQualityOptions.Select(quality => new QualityOptionViewModel(
                quality,
                ParseSelectedQualities(show.PreferredQuality).Contains(quality, StringComparer.OrdinalIgnoreCase),
                NotifyQualitySelectionChanged)));
        preferredAudioCodec = show.PreferredAudioCodec;
        minimumSeedersText = show.MinimumSeeders.ToString();
    }

    public ObservableCollection<QualityOptionViewModel> QualityOptions { get; }

    public TrackedShow Show { get; }

    public long Id => Show.Id;

    public string Title => Show.DisplayTitle;

    public string PreferredQuality => Show.PreferredQuality;

    public string PreferencesSummary =>
        $"Quality {SelectedQualitySummary} | Audio {(string.IsNullOrWhiteSpace(Show.PreferredAudioCodec) ? "Any" : Show.PreferredAudioCodec)} | Min seeders {Show.MinimumSeeders}";

    public string SelectedQualitySummary
    {
        get
        {
            var selected = GetSelectedQualities().ToList();
            return selected.Count == 0 ? "Any" : string.Join(", ", selected);
        }
    }

    public string Overview => Show.Overview ?? string.Empty;

    public string Stats => $"{Show.AvailableEpisodes}/{Show.TotalEpisodes} available | {Show.WantedEpisodes} wanted | selected {SelectedCandidateSizeDisplay}";

    public long SelectedCandidateBytes => Seasons.Sum(season => season.SelectedCandidateBytes);

    public string SelectedCandidateSizeDisplay => StorageStatusViewModel.FormatSize(SelectedCandidateBytes);

    public ObservableCollection<TrackedSeasonViewModel> Seasons { get; }

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private string preferredAudioCodec;

    [ObservableProperty]
    private string minimumSeedersText;

    [ObservableProperty]
    private string preferenceStatus = string.Empty;

    [RelayCommand]
    private void SavePreferences()
    {
        if (!int.TryParse(MinimumSeedersText, out var minimumSeeders) || minimumSeeders < 0)
        {
            PreferenceStatus = "Minimum seeders must be 0 or greater.";
            return;
        }

        var quality = string.Join(',', GetSelectedQualities());
        _savePreferences(Id, quality, PreferredAudioCodec, minimumSeeders);
        Show.PreferredQuality = quality;
        Show.PreferredAudioCodec = string.IsNullOrWhiteSpace(PreferredAudioCodec) ? string.Empty : PreferredAudioCodec.Trim();
        Show.MinimumSeeders = minimumSeeders;
        PreferenceStatus = "Saved.";
        OnPropertyChanged(nameof(PreferencesSummary));
    }

    private IEnumerable<string> GetSelectedQualities()
    {
        return QualityOptions.Where(option => option.IsSelected).Select(option => option.Label);
    }

    private void NotifyQualitySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedQualitySummary));
        OnPropertyChanged(nameof(PreferencesSummary));
    }

    private void Season_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrackedSeasonViewModel.SelectedCandidateBytes) or
            nameof(TrackedSeasonViewModel.SelectedCandidateSizeDisplay) or
            nameof(TrackedSeasonViewModel.SeasonStats))
        {
            OnPropertyChanged(nameof(Stats));
            OnPropertyChanged(nameof(SelectedCandidateBytes));
            OnPropertyChanged(nameof(SelectedCandidateSizeDisplay));
        }
    }

    private static HashSet<string> ParseSelectedQualities(string value)
    {
        var selected = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0 && !string.IsNullOrWhiteSpace(value))
        {
            selected.Add(value.Trim());
        }

        if (selected.Count == 0)
        {
            selected.Add("1080p");
        }

        return selected;
    }
}
