using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class LibraryShowDetailViewModel : ObservableObject
{
    public LibraryShowDetailViewModel(
        TrackedShow show,
        IEnumerable<LibrarySeasonViewModel> seasons,
        string episodeRecipeName,
        string packRecipeName)
    {
        Id = show.Id;
        Title = show.DisplayTitle;
        Overview = show.Overview ?? string.Empty;
        PosterPath = show.PosterPath;
        TotalEpisodes = show.TotalEpisodes;
        AvailableEpisodes = show.AvailableEpisodes;
        PreferencesSummary =
            $"Quality {show.PreferredQuality} | Audio {(string.IsNullOrWhiteSpace(show.PreferredAudioCodec) ? "Any" : show.PreferredAudioCodec)} | Min seeders {show.MinimumSeeders}";
        EpisodeRecipeName = episodeRecipeName;
        PackRecipeName = packRecipeName;
        Seasons = new ObservableCollection<LibrarySeasonViewModel>(seasons);
    }

    public long Id { get; }

    public string Title { get; }

    public string Overview { get; }

    public string? PosterPath { get; }

    public int TotalEpisodes { get; }

    public int AvailableEpisodes { get; }

    public string PreferencesSummary { get; }

    public string EpisodeRecipeName { get; }

    public string PackRecipeName { get; }

    public string RecipeSummary => $"Episode recipe: {EpisodeRecipeName} | Pack recipe: {PackRecipeName}";

    public string Stats => $"{AvailableEpisodes}/{TotalEpisodes} available | {Seasons.Count} season(s)";

    public string PosterUrl => string.IsNullOrWhiteSpace(PosterPath)
        ? string.Empty
        : $"https://image.tmdb.org/t/p/w342{PosterPath}";

    public ObservableCollection<LibrarySeasonViewModel> Seasons { get; }
}
