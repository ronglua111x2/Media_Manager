using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace media_management_app.ViewModels;

public sealed partial class LibraryViewModel : ViewModelBase
{
    private readonly ITrackedShowService _trackedShowService;
    private readonly ITrackedMovieService _trackedMovieService;
    private readonly IDatabaseService _databaseService;
    private readonly IMediaCardCatalogService _mediaCardCatalogService;
    private readonly IPosterImageService _posterImageService;
    private readonly ITorrentCartService _torrentCartService;
    private readonly ILibraryManagementService _libraryManagementService;
    private readonly IRecipeService _recipeService;

    private IReadOnlyList<LibraryMediaCardViewModel> _allMediaCards = [];
    private long? _loadedDetailMediaId;
    private MediaKind? _loadedDetailMediaKind;

    public LibraryViewModel(
        ITrackedShowService trackedShowService,
        ITrackedMovieService trackedMovieService,
        IDatabaseService databaseService,
        IMediaCardCatalogService mediaCardCatalogService,
        IPosterImageService posterImageService,
        ITorrentCartService torrentCartService,
        ILibraryManagementService libraryManagementService,
        IRecipeService recipeService)
    {
        _trackedShowService = trackedShowService;
        _trackedMovieService = trackedMovieService;
        _databaseService = databaseService;
        _mediaCardCatalogService = mediaCardCatalogService;
        _posterImageService = posterImageService;
        _torrentCartService = torrentCartService;
        _libraryManagementService = libraryManagementService;
        _recipeService = recipeService;

        _torrentCartService.CartChanged += (_, _) => RefreshCartStateOnSelectedDetail();
        RefreshLibrary();
        StatusMessage = "Select a media card to view details.";
    }

    public ObservableCollection<LibraryMediaCardViewModel> MediaCards { get; } = [];

    public IReadOnlyList<MediaCardSortMode> SortModes { get; } =
    [
        MediaCardSortMode.DateAddedDesc,
        MediaCardSortMode.TypeThenTitle,
        MediaCardSortMode.Title
    ];

    [ObservableProperty]
    private LibraryMediaCardViewModel? selectedMediaCard;

    [ObservableProperty]
    private LibraryShowDetailViewModel? selectedShow;

    [ObservableProperty]
    private LibraryMovieDetailViewModel? selectedMovie;

    [ObservableProperty]
    private ImageSource? selectedPosterImage;

    [ObservableProperty]
    private MediaCardSortMode mediaSortMode = MediaCardSortMode.DateAddedDesc;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public bool HasMedia => MediaCards.Count > 0;

    public bool HasSelectedMedia => SelectedMediaCard is not null;

    public bool IsSelectedShow => SelectedShow is not null;

    public bool IsSelectedMovie => SelectedMovie is not null;

    public bool IsDateSortSelected => MediaSortMode == MediaCardSortMode.DateAddedDesc;

    public bool IsTypeSortSelected => MediaSortMode == MediaCardSortMode.TypeThenTitle;

    public bool IsNameSortSelected => MediaSortMode == MediaCardSortMode.Title;

    [RelayCommand]
    private void RefreshLibrary()
    {
        _trackedShowService.RefreshAvailability();
        _trackedMovieService.RefreshAvailability();

        var selectedId = SelectedMediaCard?.Id;
        var selectedKind = SelectedMediaCard?.MediaKind;

        _allMediaCards = _mediaCardCatalogService.LoadCards();
        ApplyMediaCardSort();

        if (selectedId is not null && selectedKind is not null)
        {
            SelectedMediaCard = MediaCards.FirstOrDefault(card => card.Id == selectedId && card.MediaKind == selectedKind);
        }

        SelectedMediaCard ??= MediaCards.FirstOrDefault();
        OnPropertyChanged(nameof(HasMedia));
        StatusMessage = MediaCards.Count == 0
            ? "No media in library. Use Find/Add to add shows or movies."
            : $"Loaded {MediaCards.Count} media item(s).";
    }

    [RelayCommand]
    private void SetMediaSortMode(MediaCardSortMode mode)
    {
        MediaSortMode = mode;
    }

    [RelayCommand]
    private void SelectMediaCard(LibraryMediaCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        SelectedMediaCard = card;
    }

    [RelayCommand(CanExecute = nameof(CanAddMovieToCart))]
    private void AddMovieToCart(LibraryMovieDetailViewModel? movie)
    {
        if (movie is null)
        {
            return;
        }

        try
        {
            _torrentCartService.AddMovieOrder(movie.Id, movie.Title);
            StatusMessage = $"Added movie '{movie.Title}' to cart.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddEpisodeToCart))]
    private void AddEpisodeToCart(LibraryEpisodeRowViewModel? episode)
    {
        if (episode is null)
        {
            return;
        }

        try
        {
            _torrentCartService.AddEpisodeOrder(
                episode.ShowId,
                episode.Id,
                episode.SeasonNumber,
                episode.EpisodeNumber,
                episode.Title);
            StatusMessage = $"Added {episode.EpisodeCode} to cart.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddSeasonPackToCart))]
    private void AddSeasonPackToCart(LibrarySeasonViewModel? season)
    {
        if (season is null)
        {
            return;
        }

        try
        {
            _torrentCartService.AddSeasonPackOrder(season.ShowId, season.SeasonNumber);
            StatusMessage = $"Added season {season.SeasonNumber:00} pack to cart.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void AddAllMissingEpisodesToCart(LibrarySeasonViewModel? season)
    {
        if (season is null || season.IsPackMode)
        {
            return;
        }

        var addedCount = 0;
        foreach (var episode in season.Episodes.Where(episode => episode.CanAddToCart))
        {
            try
            {
                _torrentCartService.AddEpisodeOrder(
                    episode.ShowId,
                    episode.Id,
                    episode.SeasonNumber,
                    episode.EpisodeNumber,
                    episode.Title);
                addedCount++;
            }
            catch (InvalidOperationException)
            {
            }
        }

        StatusMessage = addedCount == 0
            ? $"No missing episodes in season {season.SeasonNumber:00} to add."
            : $"Added {addedCount} episode(s) from S{season.SeasonNumber:00} to cart.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMedia))]
    private void DeleteSelectedMedia()
    {
        if (SelectedMediaCard is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Delete '{SelectedMediaCard.Title}' from library?\n\nThis removes hardlinks, seasons/episodes, and clears its cart.",
            "Delete Media",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var result = _libraryManagementService.DeleteSelectedMedia(SelectedMediaCard.MediaKind, SelectedMediaCard.Id);
        SelectedMediaCard = null;
        RefreshLibrary();
        StatusMessage = result.HardlinkErrorCount > 0
            ? $"{result.Summary} {result.HardlinkErrorCount} hardlink error(s)."
            : result.Summary;
    }

    [RelayCommand(CanExecute = nameof(HasMedia))]
    private void DeleteEntireLibrary()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Delete the entire library?\n\nThis removes all hardlinks, tracked shows/movies, seasons, episodes, fetch jobs, and clears all carts.",
            "Delete Entire Library",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var result = _libraryManagementService.DeleteEntireLibrary();
        SelectedMediaCard = null;
        SelectedShow = null;
        SelectedMovie = null;
        RefreshLibrary();
        StatusMessage = result.HardlinkErrorCount > 0
            ? $"{result.Summary} {result.HardlinkErrorCount} hardlink error(s)."
            : result.Summary;
    }

    partial void OnSelectedMediaCardChanged(LibraryMediaCardViewModel? value)
    {
        foreach (var card in MediaCards)
        {
            card.IsSelected = ReferenceEquals(card, value);
        }

        var isSameMedia = value?.Id == _loadedDetailMediaId && value?.MediaKind == _loadedDetailMediaKind;
        if (!isSameMedia)
        {
            _loadedDetailMediaId = value?.Id;
            _loadedDetailMediaKind = value?.MediaKind;
            _ = LoadSelectedMediaAsync(value);
        }

        OnPropertyChanged(nameof(HasSelectedMedia));
        OnPropertyChanged(nameof(IsSelectedShow));
        OnPropertyChanged(nameof(IsSelectedMovie));
    }

    partial void OnMediaSortModeChanged(MediaCardSortMode value)
    {
        ApplyMediaCardSort();
        OnPropertyChanged(nameof(IsDateSortSelected));
        OnPropertyChanged(nameof(IsTypeSortSelected));
        OnPropertyChanged(nameof(IsNameSortSelected));
    }

    private async Task LoadSelectedMediaAsync(LibraryMediaCardViewModel? card)
    {
        var expandedSeasons = SelectedShow?.Seasons
            .Where(season => season.IsExpanded)
            .Select(season => season.SeasonNumber)
            .ToHashSet() ?? [];

        SelectedShow = null;
        SelectedMovie = null;
        SelectedPosterImage = null;

        if (card is null)
        {
            _loadedDetailMediaId = null;
            _loadedDetailMediaKind = null;
            return;
        }

        if (card.IsShow)
        {
            var show = _trackedShowService.GetShows().FirstOrDefault(item => item.Id == card.Id);
            if (show is null)
            {
                StatusMessage = "Selected show was not found.";
                return;
            }

            SelectedShow = BuildShowDetail(show, expandedSeasons);
            SelectedPosterImage = await _posterImageService.LoadAsync(card.PosterPath);
            StatusMessage = $"Viewing show: {show.DisplayTitle}";
            return;
        }

        var movie = _trackedMovieService.GetMovies().FirstOrDefault(item => item.Id == card.Id);
        if (movie is null)
        {
            StatusMessage = "Selected movie was not found.";
            return;
        }

        SelectedMovie = BuildMovieDetail(movie);
        SelectedPosterImage = await _posterImageService.LoadAsync(card.PosterPath);
        StatusMessage = $"Viewing movie: {movie.DisplayTitle}";
    }

    private void RefreshCartStateOnSelectedDetail()
    {
        if (SelectedShow is not null)
        {
            foreach (var season in SelectedShow.Seasons)
            {
                season.IsPackInCart = _torrentCartService.TryGetActiveSeasonPackOrder(
                    season.ShowId,
                    season.SeasonNumber,
                    out _);
                foreach (var episode in season.Episodes)
                {
                    episode.IsInCart = _torrentCartService.TryGetActiveEpisodeOrder(episode.Id, out _);
                }
            }
        }

        if (SelectedMovie is not null)
        {
            SelectedMovie.IsInCart = _torrentCartService.TryGetActiveMovieOrder(SelectedMovie.Id, out _);
        }

        AddMovieToCartCommand.NotifyCanExecuteChanged();
        AddEpisodeToCartCommand.NotifyCanExecuteChanged();
        AddSeasonPackToCartCommand.NotifyCanExecuteChanged();
    }

    private LibraryShowDetailViewModel BuildShowDetail(TrackedShow show, IReadOnlySet<int>? expandedSeasons = null)
    {
        var linkedEpisodeStatuses = GetLinkedEpisodeStatuses(show.TmdbId);
        var episodes = _trackedShowService.GetEpisodes(show.Id)
            .Select(episode => new LibraryEpisodeRowViewModel(episode)
            {
                LibraryLinkStatus = linkedEpisodeStatuses.TryGetValue(
                    (episode.SeasonNumber, episode.EpisodeNumber),
                    out var status)
                    ? status
                    : "Not linked",
                IsInCart = _torrentCartService.TryGetActiveEpisodeOrder(episode.Id, out _)
            })
            .ToList();

        var seasonRecords = _trackedShowService.GetSeasons(show.Id)
            .ToDictionary(season => season.SeasonNumber);

        var seasons = episodes
            .GroupBy(episode => episode.SeasonNumber)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                seasonRecords.TryGetValue(group.Key, out var seasonRecord);
                return new LibrarySeasonViewModel(
                    show.Id,
                    group.Key,
                    group,
                    UpdateSeasonManagementMode,
                    seasonRecord)
                {
                    IsExpanded = expandedSeasons?.Contains(group.Key) == true,
                    IsPackInCart = _torrentCartService.TryGetActiveSeasonPackOrder(show.Id, group.Key, out _)
                };
            });

        var episodeRecipeName = _recipeService.GetRecipeOrDefault(show.RecipeId, MediaKind.TvEpisode).Name;
        var packRecipeName = _recipeService.GetRecipeOrDefault(show.PackRecipeId, MediaKind.TvSeasonPack).Name;
        return new LibraryShowDetailViewModel(show, seasons, episodeRecipeName, packRecipeName);
    }

    private LibraryMovieDetailViewModel BuildMovieDetail(TrackedMovie movie)
    {
        var recipeName = _recipeService.GetRecipeOrDefault(movie.RecipeId, MediaKind.Movie).Name;
        return new LibraryMovieDetailViewModel(movie, recipeName)
        {
            LibraryLinkStatus = IsMovieLinked(movie.TmdbId) ? "Linked" : "Not linked",
            IsInCart = _torrentCartService.TryGetActiveMovieOrder(movie.Id, out _)
        };
    }

    private void ApplyMediaCardSort()
    {
        var sorted = MediaSortMode switch
        {
            MediaCardSortMode.TypeThenTitle => _allMediaCards
                .OrderBy(card => card.MediaKind)
                .ThenBy(card => card.Title),
            MediaCardSortMode.Title => _allMediaCards.OrderBy(card => card.Title),
            _ => _allMediaCards.OrderByDescending(card => card.CreatedUtc)
        };

        var selectedId = SelectedMediaCard?.Id;
        var selectedKind = SelectedMediaCard?.MediaKind;

        MediaCards.Clear();
        foreach (var card in sorted)
        {
            card.IsSelected = selectedId == card.Id && selectedKind == card.MediaKind;
            MediaCards.Add(card);
        }

        if (selectedId is not null && selectedKind is not null)
        {
            SelectedMediaCard = MediaCards.FirstOrDefault(card => card.Id == selectedId && card.MediaKind == selectedKind);
        }
    }

    private void UpdateSeasonManagementMode(long showId, int seasonNumber, SeasonManagementMode mode)
    {
        _trackedShowService.UpdateSeasonPackMode(showId, seasonNumber, mode);
        StatusMessage = $"Season {seasonNumber:00} set to {mode} mode.";
    }

    private Dictionary<(int SeasonNumber, int EpisodeNumber), string> GetLinkedEpisodeStatuses(int tmdbId)
    {
        var providerId = tmdbId.ToString();
        var statuses = new Dictionary<(int SeasonNumber, int EpisodeNumber), string>();
        foreach (var item in _databaseService.GetSourceItems()
                     .Where(item =>
                         item.MediaKind == MediaKind.TvEpisode &&
                         item.MatchAccepted &&
                         item.State == ItemState.Linked &&
                         !string.IsNullOrWhiteSpace(item.LinkedPath) &&
                         File.Exists(item.LinkedPath) &&
                         string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)))
        {
            var season = item.MappedSeasonNumber ?? item.SeasonNumber;
            var episode = item.MappedEpisodeNumber ?? item.EpisodeNumber;
            if (season is null || episode is null)
            {
                continue;
            }

            var key = (season.Value, episode.Value);
            var status = item.AutoTorrentLinkKind switch
            {
                AutoTorrentLinkKind.SeasonPack when item.AutoTorrentPackOwnerSeasonNumber is not null =>
                    $"Linked by pack S{item.AutoTorrentPackOwnerSeasonNumber.Value:00}",
                AutoTorrentLinkKind.Episode => "Linked by episode torrent",
                _ => "Linked"
            };

            if (!statuses.TryGetValue(key, out var existingStatus) ||
                GetLinkStatusPriority(status) > GetLinkStatusPriority(existingStatus))
            {
                statuses[key] = status;
            }
        }

        return statuses;
    }

    private static int GetLinkStatusPriority(string status)
    {
        if (status.StartsWith("Linked by pack", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return string.Equals(status, "Linked by episode torrent", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    }

    private bool IsMovieLinked(int tmdbId)
    {
        var providerId = tmdbId.ToString();
        return _databaseService.GetSourceItems().Any(item =>
            item.MediaKind == MediaKind.Movie &&
            item.MatchAccepted &&
            item.State == ItemState.Linked &&
            !string.IsNullOrWhiteSpace(item.LinkedPath) &&
            File.Exists(item.LinkedPath) &&
            string.Equals(item.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanAddMovieToCart(LibraryMovieDetailViewModel? movie) => movie?.CanAddToCart == true;

    private bool CanAddEpisodeToCart(LibraryEpisodeRowViewModel? episode) => episode?.CanAddToCart == true;

    private bool CanAddSeasonPackToCart(LibrarySeasonViewModel? season) => season?.CanAddPackToCart == true;
}
