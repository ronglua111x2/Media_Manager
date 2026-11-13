using MediaManager.App.Tests.Cleanup;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.ViewModels;

namespace MediaManager.App.Tests.Link;

/// <summary>
/// Cross-review 2, G2: an episode TMDB removed ("no longer on TMDB", kept because it had an order, a rating or a
/// thought) cannot be added to the cart from the Library, one by one or with "Add all missing".
/// </summary>
public sealed class RemovedUpstreamCartTests : IDisposable
{
    private readonly LinkTestHost _host = LinkTestHost.Create();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void LibraryRow_RemovedEpisode_CannotBeAddedToCart()
    {
        var removed = new TrackedEpisode { Id = 1, ShowId = 2, SeasonNumber = 1, EpisodeNumber = 13, Title = "Gone", RemovedUpstreamUtc = DateTime.UtcNow };
        var kept = new TrackedEpisode { Id = 2, ShowId = 2, SeasonNumber = 1, EpisodeNumber = 12, Title = "Here" };

        Assert.False(new LibraryEpisodeRowViewModel(removed).CanAddToCart);
        Assert.True(new LibraryEpisodeRowViewModel(kept).CanAddToCart);
    }

    [Fact]
    public void Cart_RefusesARemovedEpisode()
    {
        var showId = _host.AddShow(5060, "Demo Show", (1, 12, "Here"), (1, 13, "Gone"));
        var gone = _host.Db.GetTrackedEpisodes(showId).Single(e => e.EpisodeNumber == 13);
        _host.Db.MarkTrackedEpisodeRemovedUpstream(gone.Id, DateTime.UtcNow);
        var cart = new TorrentCartService(_host.Db, new FakeAppLogger());

        Assert.Throws<InvalidOperationException>(() => cart.AddEpisodeOrder(showId, gone.Id, 1, 13, "Gone"));
        var here = _host.Db.GetTrackedEpisodes(showId).Single(e => e.EpisodeNumber == 12);
        Assert.NotNull(cart.AddEpisodeOrder(showId, here.Id, 1, 12, "Here"));
    }
}
