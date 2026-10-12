using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;

namespace media_management_app.ViewModels;

public sealed partial class EpisodeRatingSeasonTabViewModel : ObservableObject
{
    public EpisodeRatingSeasonTabViewModel(int seasonNumber)
    {
        SeasonNumber = seasonNumber;
    }

    public int SeasonNumber { get; }

    public string Label => AppConstants.FormatSeasonShortLabel(SeasonNumber);

    [ObservableProperty]
    private bool isSelected;
}
