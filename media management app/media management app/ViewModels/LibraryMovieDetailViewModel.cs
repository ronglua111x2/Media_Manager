using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class LibraryMovieDetailViewModel : ObservableObject
{
    public LibraryMovieDetailViewModel(TrackedMovie movie, string recipeName)
    {
        Id = movie.Id;
        Title = movie.DisplayTitle;
        RecipeName = recipeName;
        Overview = movie.Overview ?? string.Empty;
        PosterPath = movie.PosterPath;
        Availability = movie.Availability;
        TorrentHash = movie.TorrentHash ?? string.Empty;
        TorrentState = movie.TorrentState ?? string.Empty;
        TorrentProgress = movie.TorrentProgress;
        PreferencesSummary =
            $"Quality {movie.PreferredQuality} | Audio {(string.IsNullOrWhiteSpace(movie.PreferredAudioCodec) ? "Any" : movie.PreferredAudioCodec)} | Min seeders {movie.MinimumSeeders}";
    }

    public long Id { get; }

    public string Title { get; }

    public string Overview { get; }

    public string? PosterPath { get; }

    public EpisodeAvailability Availability { get; }

    public string TorrentHash { get; }

    public string TorrentState { get; }

    public double TorrentProgress { get; }

    public bool HasTorrent => !string.IsNullOrWhiteSpace(TorrentHash);

    public bool IsTorrentComplete => HasTorrent && TorrentProgress >= 0.999;

    public string PreferencesSummary { get; }

    public string RecipeName { get; }

    public string RecipeSummary => $"Recipe: {RecipeName}";

    public bool IsAvailable => Availability == EpisodeAvailability.Available;

    public bool CanAddToCart => !IsAvailable && !IsInCart && !HasTorrent;

    public bool IsLinked => !string.Equals(LibraryLinkStatus, "Not linked", StringComparison.OrdinalIgnoreCase);

    public bool CanLink => HasTorrent || IsLinked;

    public string LinkActionLabel => IsLinked ? "Unlink" : "Link";

    public string LinkActionIconKind => IsLinked ? "Unlink" : "Link";

    public string LinkActionToolTip => IsLinked
        ? "Remove generated library hardlink for this movie"
        : "Create library hardlink for this movie";

    public string CompactLinkStatus => IsLinked ? "Linked" : "Not linked";

    public bool ShowWorkflowStatus => !string.IsNullOrWhiteSpace(WorkflowStatusLabel) &&
                                      !string.Equals(WorkflowStatusLabel, "Linked", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToCart))]
    [NotifyPropertyChangedFor(nameof(WorkflowStatusLabel))]
    [NotifyPropertyChangedFor(nameof(ShowWorkflowStatus))]
    private bool isInCart;

    public string AvailabilityLabel => IsAvailable ? "Available" : "Not in library folder";

    public string WorkflowStatusLabel
    {
        get
        {
            if (IsLinked)
            {
                return "Linked";
            }

            if (IsTorrentComplete)
            {
                return "Ready to link";
            }

            if (HasTorrent)
            {
                return TorrentProgress > 0 ? "Downloading" : "Added";
            }

            return IsInCart ? "In cart" : string.Empty;
        }
    }

    public string PosterUrl => string.IsNullOrWhiteSpace(PosterPath)
        ? string.Empty
        : $"https://image.tmdb.org/t/p/w342{PosterPath}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLinked))]
    [NotifyPropertyChangedFor(nameof(CanLink))]
    [NotifyPropertyChangedFor(nameof(LinkActionLabel))]
    [NotifyPropertyChangedFor(nameof(LinkActionIconKind))]
    [NotifyPropertyChangedFor(nameof(LinkActionToolTip))]
    [NotifyPropertyChangedFor(nameof(WorkflowStatusLabel))]
    [NotifyPropertyChangedFor(nameof(CompactLinkStatus))]
    [NotifyPropertyChangedFor(nameof(ShowWorkflowStatus))]
    private string libraryLinkStatus = "Not linked";
}
