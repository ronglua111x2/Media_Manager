using System.Reflection;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.ViewModels;
using MediaManager.App.Tests.Cleanup;
using MediaManager.App.Tests.Link;

namespace MediaManager.App.Tests.Services;

public class FindQuickAddPlannerTests
{
    private static readonly DateTime Today = new(2026, 11, 24);

    private static TrackedEpisode Episode(
        long id,
        int season,
        int episode,
        int airedDaysAgo = 30,
        EpisodeAvailability availability = EpisodeAvailability.Missing) => new()
    {
        Id = id,
        ShowId = 1,
        SeasonNumber = season,
        EpisodeNumber = episode,
        Title = $"Episode {episode}",
        AirDate = Today.AddDays(-airedDaysAgo),
        Availability = availability
    };

    private static readonly IReadOnlyList<TrackedEpisode> Show =
    [
        Episode(1, 0, 1),                                        // special: skipped
        Episode(2, 1, 1, availability: EpisodeAvailability.Available),
        Episode(3, 1, 2),
        Episode(4, 2, 1),
        Episode(5, 2, 2),
        Episode(6, 3, 1, airedDaysAgo: -7)                       // not aired yet
    ];

    [Fact]
    public void AllSeasonPacks_OnePackPerAiredSeason_SkipsSpecialsAndUnaired()
    {
        var plan = FindQuickAddPlanner.PlanShow(QuickAddShowOption.AllSeasonPacks, Show, 3, Today);

        Assert.All(plan.Orders, order => Assert.True(order.IsSeasonPack));
        Assert.Equal([1, 2], plan.Orders.Select(order => order.SeasonNumber));
    }

    [Fact]
    public void LatestSeason_IsLatestAiredSeason()
    {
        var plan = FindQuickAddPlanner.PlanShow(QuickAddShowOption.LatestSeasonPack, Show, 3, Today);

        var order = Assert.Single(plan.Orders);
        Assert.True(order.IsSeasonPack);
        Assert.Equal(2, order.SeasonNumber);
    }

    [Fact]
    public void MissingEpisodes_OnlyMissingAiredNonSpecialEpisodes()
    {
        var plan = FindQuickAddPlanner.PlanShow(QuickAddShowOption.MissingEpisodes, Show, 3, Today);

        Assert.False(plan.FellBackToPacks);
        Assert.Equal([3L, 4L, 5L], plan.Orders.Select(order => order.Episode!.Id));
    }

    [Fact]
    public void MissingEpisodes_AboveCap_FallsBackToPacks()
    {
        var plan = FindQuickAddPlanner.PlanShow(QuickAddShowOption.MissingEpisodes, Show, 3, Today, maxEpisodeOrders: 2);

        Assert.True(plan.FellBackToPacks);
        Assert.All(plan.Orders, order => Assert.True(order.IsSeasonPack));
        Assert.Equal([1, 2], plan.Orders.Select(order => order.SeasonNumber));
    }

    [Fact]
    public void NoEpisodesLoadedYet_UsesSeasonCount()
    {
        var packs = FindQuickAddPlanner.PlanShow(QuickAddShowOption.AllSeasonPacks, [], 3, Today);
        var latest = FindQuickAddPlanner.PlanShow(QuickAddShowOption.LatestSeasonPack, [], 3, Today);
        var missing = FindQuickAddPlanner.PlanShow(QuickAddShowOption.MissingEpisodes, [], 2, Today);

        Assert.Equal([1, 2, 3], packs.Orders.Select(order => order.SeasonNumber));
        Assert.Equal(3, Assert.Single(latest.Orders).SeasonNumber);
        Assert.Equal([1, 2], missing.Orders.Select(order => order.SeasonNumber));
        Assert.All(missing.Orders, order => Assert.True(order.IsSeasonPack));
    }

    [Fact]
    public void LibraryOnly_QueuesNothing()
    {
        Assert.Empty(FindQuickAddPlanner.PlanShow(QuickAddShowOption.LibraryOnly, Show, 3, Today).Orders);
    }
}

/// <summary>The Find add / "Add + search" flow with the library, cart and dialogs faked.</summary>
public class FindAddQuickAddFlowTests
{
    private sealed class FakeDialogs : IFindAddDialogs
    {
        public int ConfirmCalls { get; private set; }

        public int PickerCalls { get; private set; }

        public QuickAddShowOption? PickerAnswer { get; set; } = QuickAddShowOption.AllSeasonPacks;

        public bool ConfirmAdd(string mediaTitle)
        {
            ConfirmCalls++;
            return true;
        }

        public bool TryPickEpisodeOrganization(
            string mediaTitle,
            int seasonCount,
            int episodeCount,
            IReadOnlyList<TmdbEpisodeGroupSummary> episodeGroups,
            out string? episodeGroupId,
            out string? episodeGroupName)
        {
            episodeGroupId = null;
            episodeGroupName = null;
            return true;
        }

        public QuickAddShowOption? PickQuickAddShowOption(string mediaTitle, bool alreadyInLibrary)
        {
            PickerCalls++;
            return PickerAnswer;
        }
    }

    private sealed class FakeLauncher : ICartSearchLauncher
    {
        public List<(MediaKind Kind, long MediaId, IReadOnlyCollection<long> OrderIds)> Queued { get; } = [];

        public void QueueSearch(MediaKind kind, long mediaId, string title, IReadOnlyCollection<long> orderIds) =>
            Queued.Add((kind, mediaId, orderIds));

        public void OpenCart(MediaKind kind, long mediaId)
        {
        }

        public event EventHandler<CartSearchFinishedEventArgs>? SearchFinished
        {
            add { }
            remove { }
        }
    }

    private readonly FakeDialogs _dialogs = new();
    private readonly FakeLauncher _launcher = new();
    private readonly List<string> _libraryCalls = [];
    private readonly List<string> _cartAdds = [];
    private readonly List<TrackedShow> _shows = [];
    private readonly List<TrackedMovie> _movies = [];
    private readonly List<TrackedEpisode> _episodes = [];
    private readonly HashSet<int> _seasonPacksInCart = [];
    private long _nextOrderId = 100;

    /// <summary>Default for anything not handled: empty lists, completed tasks.</summary>
    private static object? Default(MethodInfo method)
    {
        var type = method.ReturnType;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = type.GetGenericArguments()[0];
            object? value = inner.IsGenericType && inner.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                ? Array.CreateInstance(inner.GetGenericArguments()[0], 0)
                : inner.IsValueType ? Activator.CreateInstance(inner) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [value]);
        }

        return null;
    }

    private FindAddViewModel CreateViewModel()
    {
        var shows = TestFake<ITrackedShowService>.Create((method, args) => method.Name switch
        {
            nameof(ITrackedShowService.GetShows) => _shows.ToList(),
            nameof(ITrackedShowService.GetEpisodes) => _episodes.ToList(),
            nameof(ITrackedShowService.AddShowAsync) => AddShow((TmdbShowSearchResult)args![0]!),
            _ => Default(method)
        });
        var movies = TestFake<ITrackedMovieService>.Create((method, args) => method.Name switch
        {
            nameof(ITrackedMovieService.GetMovies) => _movies.ToList(),
            nameof(ITrackedMovieService.AddMovieAsync) => AddMovie((TmdbMovieSearchResult)args![0]!),
            _ => Default(method)
        });
        var recipes = TestFake<IRecipeService>.Create((method, _) => method.Name switch
        {
            nameof(IRecipeService.GetDefaultRecipe) => new SearchRecipe { RecipeId = "default" },
            _ => Default(method)
        });
        var cart = TestFake<ITorrentCartService>.Create((method, args) =>
        {
            switch (method.Name)
            {
                case nameof(ITorrentCartService.TryGetActiveSeasonPackOrder):
                    args![2] = null;
                    return _seasonPacksInCart.Contains((int)args[1]!);
                case nameof(ITorrentCartService.TryGetActiveEpisodeOrder):
                case nameof(ITorrentCartService.TryGetActiveMovieOrder):
                    args![1] = null;
                    return false;
                case nameof(ITorrentCartService.AddSeasonPackOrder):
                    _cartAdds.Add($"pack S{(int)args![1]!:00}");
                    return new TorrentCartOrder { Id = _nextOrderId++ };
                case nameof(ITorrentCartService.AddEpisodeOrder):
                    _cartAdds.Add($"episode {(long)args![1]!}");
                    return new TorrentCartOrder { Id = _nextOrderId++ };
                case nameof(ITorrentCartService.AddMovieOrder):
                    _cartAdds.Add("movie");
                    return new TorrentCartOrder { Id = _nextOrderId++ };
                default:
                    return Default(method);
            }
        });

        return new FindAddViewModel(
            shows,
            movies,
            TestFake<ITmdbShowCatalogService>.Create((method, _) => Default(method)),
            TestFake<ITmdbMovieCatalogService>.Create((method, _) => Default(method)),
            recipes,
            new FakeAppLogger(),
            cart,
            _launcher,
            _dialogs);
    }

    private Task<TrackedShow> AddShow(TmdbShowSearchResult result)
    {
        _libraryCalls.Add("add show");
        var show = new TrackedShow { Id = 7, TmdbId = result.TmdbId, Title = result.Title };
        _shows.Add(show);
        return Task.FromResult(show);
    }

    private Task<TrackedMovie> AddMovie(TmdbMovieSearchResult result)
    {
        _libraryCalls.Add("add movie");
        var movie = new TrackedMovie { Id = 9, TmdbId = result.TmdbId, Title = result.Title };
        _movies.Add(movie);
        return Task.FromResult(movie);
    }

    private static TmdbUnifiedSearchResult ShowResult(bool alreadyAdded = false) => new()
    {
        TmdbId = 501,
        Title = "Show 01",
        MediaKind = MediaKind.TvEpisode,
        SeasonCount = 2,
        EpisodeCount = 20,
        IsAlreadyAdded = alreadyAdded
    };

    private static TmdbUnifiedSearchResult MovieResult() => new()
    {
        TmdbId = 601,
        Title = "Movie 01",
        MediaKind = MediaKind.Movie
    };

    [Fact]
    public void ConfirmDialog_IsOffByDefault()
    {
        Assert.False(FindAddViewModel.ConfirmBeforeAdd);
    }

    [Fact]
    public async Task AddToLibrary_AddsWithoutConfirmDialog()
    {
        var vm = CreateViewModel();
        vm.SelectedResult = MovieResult();

        await vm.AddSelectedToLibraryCommand.ExecuteAsync(null);

        Assert.Equal(0, _dialogs.ConfirmCalls);
        Assert.Equal(["add movie"], _libraryCalls);
        Assert.Empty(_cartAdds);
        Assert.Empty(_launcher.Queued);
    }

    [Fact]
    public async Task QuickAdd_Movie_QueuesExactlyOneMovieOrderAndSearchesIt()
    {
        var vm = CreateViewModel();
        vm.SelectedResult = MovieResult();

        await vm.QuickAddSelectedCommand.ExecuteAsync(null);

        Assert.Equal(["add movie"], _libraryCalls);
        Assert.Equal(["movie"], _cartAdds);
        var queued = Assert.Single(_launcher.Queued);
        Assert.Equal(MediaKind.Movie, queued.Kind);
        Assert.Equal(9, queued.MediaId);
        Assert.Single(queued.OrderIds);
        Assert.Equal(0, _dialogs.PickerCalls);
        Assert.True(vm.CanOpenQuickAddCart);
    }

    [Fact]
    public async Task QuickAdd_CancelInPicker_AddsAndQueuesNothing()
    {
        _dialogs.PickerAnswer = null;
        var vm = CreateViewModel();
        vm.SelectedResult = ShowResult();

        await vm.QuickAddSelectedCommand.ExecuteAsync(null);

        Assert.Empty(_libraryCalls);
        Assert.Empty(_cartAdds);
        Assert.Empty(_launcher.Queued);
        Assert.False(vm.CanOpenQuickAddCart);
    }

    [Fact]
    public async Task QuickAdd_Show_SkipsPacksAlreadyInCart()
    {
        // Metadata not loaded yet (no episodes): packs come from the result's season count.
        _seasonPacksInCart.Add(1);
        var vm = CreateViewModel();
        vm.SelectedResult = ShowResult();

        await vm.QuickAddSelectedCommand.ExecuteAsync(null);

        Assert.Equal(["add show"], _libraryCalls);
        Assert.Equal(["pack S02"], _cartAdds);
        var queued = Assert.Single(_launcher.Queued);
        Assert.Equal(7, queued.MediaId);
        Assert.Single(queued.OrderIds);
    }

    [Fact]
    public async Task QuickAdd_LibraryOnly_AddsButQueuesNothing()
    {
        _dialogs.PickerAnswer = QuickAddShowOption.LibraryOnly;
        var vm = CreateViewModel();
        vm.SelectedResult = ShowResult();

        await vm.QuickAddSelectedCommand.ExecuteAsync(null);

        Assert.Equal(["add show"], _libraryCalls);
        Assert.Empty(_cartAdds);
        Assert.Empty(_launcher.Queued);
    }

    [Fact]
    public async Task SearchNow_AlreadyInLibrary_DoesNotAddAgainButQueuesOrders()
    {
        _shows.Add(new TrackedShow { Id = 7, TmdbId = 501, Title = "Show 01" });
        _episodes.Add(new TrackedEpisode
        {
            Id = 70, ShowId = 7, SeasonNumber = 1, EpisodeNumber = 1, Title = "Episode 1",
            AirDate = DateTime.Today.AddDays(-10)
        });
        _dialogs.PickerAnswer = QuickAddShowOption.MissingEpisodes;
        var vm = CreateViewModel();
        vm.SelectedResult = ShowResult(alreadyAdded: true);
        Assert.Equal("Search now", vm.QuickAddButtonText);

        await vm.QuickAddSelectedCommand.ExecuteAsync(null);

        Assert.Empty(_libraryCalls);
        Assert.Equal(["episode 70"], _cartAdds);
        Assert.Single(_launcher.Queued);
    }

    [Fact]
    public async Task QuickAdd_NothingNew_DoesNotStartASearch()
    {
        _seasonPacksInCart.Add(1);
        _seasonPacksInCart.Add(2);
        var vm = CreateViewModel();
        vm.SelectedResult = ShowResult();

        await vm.QuickAddSelectedCommand.ExecuteAsync(null);

        Assert.Equal(["add show"], _libraryCalls);
        Assert.Empty(_cartAdds);
        Assert.Empty(_launcher.Queued);
    }
}
