using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Search;

/// <summary>
/// The log lines written after a cart search returns. Without them a finished run, a run with no candidates and a
/// run that failed on an exception all ended at "Recipe search query succeeded N/N" in the system log.
/// </summary>
public class CartRunLogTests
{
    private static RecipeCandidateResult Accepted(string name, int score) => new()
    {
        SearchResult = new TorrentSearchResult { FileName = name },
        TotalScore = score
    };

    private static RecipeCandidateResult Rejected(CandidateRejectReason reason) => new()
    {
        SearchResult = new TorrentSearchResult { FileName = $"rejected {reason}" },
        RejectReason = reason
    };

    [Fact]
    public void Evaluation_counts_rows_and_names_the_top_candidate_and_main_reject_reasons()
    {
        var best = Accepted("Movie 01 2021 1080p BluRay", 900);
        var evaluated = new List<RecipeCandidateResult>
        {
            Accepted("Movie 01 2021 720p WEB", 300),
            best,
            Rejected(CandidateRejectReason.TitleMismatch),
            Rejected(CandidateRejectReason.TitleMismatch),
            Rejected(CandidateRejectReason.TitleMismatch),
            Rejected(CandidateRejectReason.SeedersTooLow),
            Rejected(CandidateRejectReason.QualityMismatch),
            Rejected(CandidateRejectReason.QualityMismatch)
        };
        var result = new RecipeDryRunResult { AcceptedCandidates = [best, evaluated[0]] };

        var line = CartRunLog.Evaluation("Movie 01 (2021)", evaluated, result);

        Assert.Equal(
            "Recipe filtering for 'Movie 01 (2021)': rows=8, accepted=2, rejected=6, kept=2, " +
            "top='Movie 01 2021 1080p BluRay' score=900; rejects: TitleMismatch=3, QualityMismatch=2, SeedersTooLow=1.",
            line);
    }

    [Fact]
    public void Evaluation_with_nothing_accepted_shows_the_rejects_and_the_source_problem()
    {
        var evaluated = new List<RecipeCandidateResult>
        {
            Rejected(CandidateRejectReason.YearMismatch),
            Rejected(CandidateRejectReason.TitleMismatch),
            Rejected(CandidateRejectReason.SizeTooLarge),
            Rejected(CandidateRejectReason.ExcludedTerm),
            Rejected(CandidateRejectReason.Blacklisted),
            Rejected(CandidateRejectReason.Blacklisted)
        };
        var result = new RecipeDryRunResult();

        var line = CartRunLog.Evaluation("Show 01 S01E02", evaluated, result);

        Assert.StartsWith("Recipe filtering for 'Show 01 S01E02': rows=6, accepted=0, rejected=6, kept=0; rejects: Blacklisted=2, ", line);
        Assert.EndsWith(", +1 more.", line);
        Assert.DoesNotContain("top=", line);
    }

    [Fact]
    public void Evaluation_of_an_empty_search_reports_the_source_problem()
    {
        var result = new RecipeDryRunResult { SearchSourceProblem = "No search site answered." };

        Assert.Equal(
            "Recipe filtering for 'Movie 02': rows=0, accepted=0, rejected=0, kept=0; source problem: No search site answered.",
            CartRunLog.Evaluation("Movie 02", [], result));
    }

    [Fact]
    public void Order_lines_name_the_order_and_its_target()
    {
        var movie = new TorrentCartOrder { Id = 7, TargetKind = MediaKind.Movie, Title = "Movie 03" };
        var pack = new TorrentCartOrder { Id = 8, TargetKind = MediaKind.TvSeasonPack, Title = "Show 02", SeasonNumber = 2 };
        var episode = new TorrentCartOrder { Id = 9, TargetKind = MediaKind.TvEpisode, Title = "Show 02", EpisodeId = 5, SeasonNumber = 1, EpisodeNumber = 4 };

        Assert.Equal("Cart order id=7 'Movie 03' (movie): saved 3 candidate(s).", CartRunLog.OrderFound(movie, 3, null));
        Assert.Equal(
            "Cart order id=8 'Show 02' (pack S02): no candidates. Nothing passed the recipe. Backend: native paused",
            CartRunLog.OrderNoCandidates(pack, "Nothing passed the recipe.", "native paused"));
        Assert.Equal(
            "Cart order id=9 'Show 02' (S01E04) failed after search: InvalidOperationException: Tracked show was not found.",
            CartRunLog.OrderFailed(episode, new InvalidOperationException("Tracked show was not found.")));
    }

    [Fact]
    public void Failure_message_is_flattened_and_capped()
    {
        var order = new TorrentCartOrder { Id = 1, TargetKind = MediaKind.Movie, Title = "Movie 04" };
        var message = "first line\r\nsecond line " + new string('x', 400);

        var line = CartRunLog.OrderFailed(order, new Exception(message));

        Assert.DoesNotContain('\n', line);
        Assert.Contains("first line second line", line);
        Assert.EndsWith("…", line);
    }

    [Fact]
    public void Run_complete_matches_the_status_bar_text()
    {
        Assert.Equal("Search complete. Candidates=1, No candidates=2, Failed=3.", CartRunLog.RunComplete(1, 2, 3));
    }
}
