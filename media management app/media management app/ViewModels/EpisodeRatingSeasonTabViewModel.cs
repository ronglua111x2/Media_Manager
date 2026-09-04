using CommunityToolkit.Mvvm.ComponentModel;

namespace media_management_app.ViewModels;

public sealed partial class EpisodeRatingSeasonTabViewModel : ObservableObject
{
    public EpisodeRatingSeasonTabViewModel(int seasonNumber)
    {
        SeasonNumber = seasonNumber;
    }

    public int SeasonNumber { get; }

    public string Label => $"S{SeasonNumber}";

    [ObservableProperty]
    private bool isSelected;
}
