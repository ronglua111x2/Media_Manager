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
        PreferencesSummary =
            $"Quality {movie.PreferredQuality} | Audio {(string.IsNullOrWhiteSpace(movie.PreferredAudioCodec) ? "Any" : movie.PreferredAudioCodec)} | Min seeders {movie.MinimumSeeders}";
    }

    public long Id { get; }

    public string Title { get; }

    public string Overview { get; }

    public string? PosterPath { get; }

    public EpisodeAvailability Availability { get; }

    public string PreferencesSummary { get; }

    public string RecipeName { get; }

    public string RecipeSummary => $"Recipe: {RecipeName}";

    public bool IsAvailable => Availability == EpisodeAvailability.Available;

    public bool CanAddToCart => !IsAvailable && !IsInCart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToCart))]
    private bool isInCart;

    public string AvailabilityLabel => IsAvailable ? "Available" : "Not in library folder";

    public string PosterUrl => string.IsNullOrWhiteSpace(PosterPath)
        ? string.Empty
        : $"https://image.tmdb.org/t/p/w342{PosterPath}";

    [ObservableProperty]
    private string libraryLinkStatus = "Not linked";
}
