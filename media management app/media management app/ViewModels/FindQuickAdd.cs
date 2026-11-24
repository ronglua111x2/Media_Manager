using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using media_management_app.Views;

namespace media_management_app.ViewModels;

/// <summary>What Find "Add + search" queues for a show.</summary>
public enum QuickAddShowOption
{
    /// <summary>One season pack per aired season (season 0 skipped).</summary>
    AllSeasonPacks,

    /// <summary>One season pack for the latest aired season.</summary>
    LatestSeasonPack,

    /// <summary>One episode order per missing aired episode; season packs above the cap.</summary>
    MissingEpisodes,

    /// <summary>Add to the library only; queue nothing.</summary>
    LibraryOnly
}

/// <summary>A library item a quick-add queues orders for.</summary>
public sealed record QuickAddTarget(MediaKind Kind, long Id, string Title);

/// <summary>One cart order the quick-add wants: an episode order or a season pack order.</summary>
public sealed record QuickAddOrderRequest(int SeasonNumber, TrackedEpisode? Episode)
{
    public bool IsSeasonPack => Episode is null;
}

public sealed record QuickAddPlan(IReadOnlyList<QuickAddOrderRequest> Orders, bool FellBackToPacks);

/// <summary>Pure order selection for Find "Add + search" (no cart or UI access).</summary>
public static class FindQuickAddPlanner
{
    /// <summary>Above this many missing aired episodes, queue season packs instead of episodes.</summary>
    public const int MaxEpisodeOrders = 24;

    /// <param name="episodes">Tracked episodes of the show; may be empty when metadata is not loaded yet.</param>
    /// <param name="fallbackSeasonCount">TMDB season count, used when <paramref name="episodes"/> is empty.</param>
    /// <param name="today">Local date; episodes airing after it are not aired yet.</param>
    public static QuickAddPlan PlanShow(
        QuickAddShowOption option,
        IReadOnlyList<TrackedEpisode> episodes,
        int fallbackSeasonCount,
        DateTime today,
        int maxEpisodeOrders = MaxEpisodeOrders)
    {
        if (option == QuickAddShowOption.LibraryOnly)
        {
            return new QuickAddPlan([], false);
        }

        var aired = episodes
            .Where(episode => episode.SeasonNumber != AppConstants.SpecialsSeasonNumber &&
                              !episode.IsRemovedUpstream &&
                              episode.AirDate is { } airDate &&
                              airDate.Date <= today.Date)
            .ToList();

        // Metadata may still be loading: fall back to the season count from the search result.
        IReadOnlyList<int> airedSeasons = episodes.Count == 0
            ? Enumerable.Range(1, Math.Max(0, fallbackSeasonCount)).ToList()
            : aired.Select(episode => episode.SeasonNumber).Distinct().OrderBy(season => season).ToList();

        switch (option)
        {
            case QuickAddShowOption.AllSeasonPacks:
                return new QuickAddPlan(Packs(airedSeasons), false);

            case QuickAddShowOption.LatestSeasonPack:
                return new QuickAddPlan(airedSeasons.Count == 0 ? [] : Packs([airedSeasons[^1]]), false);

            case QuickAddShowOption.MissingEpisodes:
                if (episodes.Count == 0)
                {
                    // No episode rows to order yet; packs are the only thing we can search.
                    return new QuickAddPlan(Packs(airedSeasons), airedSeasons.Count > 0);
                }

                var missing = aired
                    .Where(episode => episode.Availability == EpisodeAvailability.Missing)
                    .OrderBy(episode => episode.SeasonNumber)
                    .ThenBy(episode => episode.EpisodeNumber)
                    .ToList();
                if (missing.Count > maxEpisodeOrders)
                {
                    var seasons = missing.Select(episode => episode.SeasonNumber).Distinct().OrderBy(season => season).ToList();
                    return new QuickAddPlan(Packs(seasons), true);
                }

                return new QuickAddPlan(
                    missing.Select(episode => new QuickAddOrderRequest(episode.SeasonNumber, episode)).ToList(),
                    false);

            default:
                return new QuickAddPlan([], false);
        }
    }

    private static List<QuickAddOrderRequest> Packs(IEnumerable<int> seasons) =>
        seasons.Select(season => new QuickAddOrderRequest(season, null)).ToList();
}

/// <summary>Dialogs Find/Add shows; a seam so the add flow can be tested without WPF windows.</summary>
public interface IFindAddDialogs
{
    /// <summary>The typed "are you sure" lock. Only shown when <see cref="FindAddViewModel.ConfirmBeforeAdd"/> is on.</summary>
    bool ConfirmAdd(string mediaTitle);

    /// <summary>Episode order choice. Returns false on cancel.</summary>
    bool TryPickEpisodeOrganization(
        string mediaTitle,
        int seasonCount,
        int episodeCount,
        IReadOnlyList<TmdbEpisodeGroupSummary> episodeGroups,
        out string? episodeGroupId,
        out string? episodeGroupName);

    /// <summary>What to queue for a show. Null on cancel.</summary>
    QuickAddShowOption? PickQuickAddShowOption(string mediaTitle, bool alreadyInLibrary);
}

public sealed class WpfFindAddDialogs : IFindAddDialogs
{
    public bool ConfirmAdd(string mediaTitle)
    {
        var dialog = new AddMediaLockDialog(mediaTitle) { Owner = System.Windows.Application.Current.MainWindow };
        return dialog.ShowDialog() == true;
    }

    public bool TryPickEpisodeOrganization(
        string mediaTitle,
        int seasonCount,
        int episodeCount,
        IReadOnlyList<TmdbEpisodeGroupSummary> episodeGroups,
        out string? episodeGroupId,
        out string? episodeGroupName)
    {
        var dialog = new EpisodeOrganizationDialog(mediaTitle, seasonCount, episodeCount, episodeGroups)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true)
        {
            episodeGroupId = null;
            episodeGroupName = null;
            return false;
        }

        episodeGroupId = dialog.SelectedEpisodeGroupId;
        episodeGroupName = dialog.SelectedEpisodeGroupName;
        return true;
    }

    public QuickAddShowOption? PickQuickAddShowOption(string mediaTitle, bool alreadyInLibrary)
    {
        var dialog = new QuickAddShowDialog(mediaTitle, alreadyInLibrary)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? dialog.SelectedOption : null;
    }
}
