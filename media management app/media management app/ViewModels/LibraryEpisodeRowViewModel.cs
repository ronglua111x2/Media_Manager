using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class LibraryEpisodeRowViewModel : ObservableObject
{
    public LibraryEpisodeRowViewModel(TrackedEpisode episode)
    {
        Id = episode.Id;
        ShowId = episode.ShowId;
        SeasonNumber = episode.SeasonNumber;
        EpisodeNumber = episode.EpisodeNumber;
        Title = episode.Title;
        AirDateDisplay = episode.AirDateDisplay;
        Availability = episode.Availability;
        TorrentHash = episode.TorrentHash ?? string.Empty;
    }

    public long Id { get; }

    public long ShowId { get; }

    public int SeasonNumber { get; }

    public int EpisodeNumber { get; }

    public string EpisodeCode => $"S{SeasonNumber:00}E{EpisodeNumber:00}";

    public string Title { get; }

    public string AirDateDisplay { get; }

    public EpisodeAvailability Availability { get; }

    public bool IsAvailable => Availability == EpisodeAvailability.Available;

    public string TorrentHash { get; }

    public bool CanAddToCart => !IsSeasonPackMode && !IsAvailable && !IsInCart;

    public bool CanLink => !IsSeasonPackMode && !string.IsNullOrWhiteSpace(TorrentHash);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToCart))]
    private bool isInCart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToCart))]
    [NotifyPropertyChangedFor(nameof(CanLink))]
    private bool isSeasonPackMode;

    public string AvailabilityLabel => IsAvailable ? "Available" : "Missing";

    [ObservableProperty]
    private string libraryLinkStatus = "Not linked";
}
