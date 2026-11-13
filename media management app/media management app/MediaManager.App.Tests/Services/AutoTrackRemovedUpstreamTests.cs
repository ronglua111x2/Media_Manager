using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Services;

/// <summary>
/// A row TMDB no longer lists is kept and flagged when it is in use (audit L3-09). Auto-Track must not hunt it as a
/// missing episode, and it must not keep the show from counting as caught up.
/// </summary>
public class AutoTrackRemovedUpstreamTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0);

    private static readonly TrackedShow Show = new()
    {
        Id = 1,
        Title = "Demo Show",
        AutoTrackFromSeason = 1,
        AutoTrackFromEpisode = 1
    };

    private static TrackedEpisode Episode(long id, int number, EpisodeAvailability availability, bool removedUpstream = false) => new()
    {
        Id = id,
        ShowId = 1,
        SeasonNumber = 1,
        EpisodeNumber = number,
        AirDate = new DateTime(2026, 10, 1),
        Availability = availability,
        RemovedUpstreamUtc = removedUpstream ? new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) : null
    };

    [Fact]
    public void FindPendingEpisodes_SkipsEpisodeRemovedUpstream()
    {
        TrackedEpisode[] episodes =
        [
            Episode(1, 1, EpisodeAvailability.Available),
            Episode(2, 2, EpisodeAvailability.Missing, removedUpstream: true),
            Episode(3, 3, EpisodeAvailability.Missing)
        ];

        var pending = AutoTrackTmdbEligibility.FindPendingEpisodes(Show, episodes, _ => false, Now);

        Assert.Equal([3L], pending.Select(episode => episode.Id));
    }

    [Fact]
    public void FindLatestPendingEpisode_RemovedUpstreamOnly_IsNull()
    {
        TrackedEpisode[] episodes =
        [
            Episode(1, 1, EpisodeAvailability.Available),
            Episode(2, 2, EpisodeAvailability.Missing, removedUpstream: true)
        ];

        Assert.Null(AutoTrackTmdbEligibility.FindLatestPendingEpisode(Show, episodes, _ => false, Now));
        Assert.False(AutoTrackTmdbEligibility.HasPendingLatestEpisode(Show, episodes, _ => false, Now));
    }

    [Fact]
    public void IsFullyCaughtUp_IgnoresMissingEpisodeRemovedUpstream()
    {
        TrackedEpisode[] episodes =
        [
            Episode(1, 1, EpisodeAvailability.Available),
            Episode(2, 2, EpisodeAvailability.Missing, removedUpstream: true),
            Episode(3, 3, EpisodeAvailability.Available)
        ];

        Assert.True(AutoTrackTmdbEligibility.IsFullyCaughtUp(Show, episodes));
    }

    [Fact]
    public void IsFullyCaughtUp_StillWaitsForAListedMissingEpisode()
    {
        TrackedEpisode[] episodes =
        [
            Episode(1, 1, EpisodeAvailability.Available, removedUpstream: true),
            Episode(2, 2, EpisodeAvailability.Missing)
        ];

        Assert.False(AutoTrackTmdbEligibility.IsFullyCaughtUp(Show, episodes));
    }
}
