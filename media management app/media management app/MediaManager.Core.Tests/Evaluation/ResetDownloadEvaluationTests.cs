using FluentAssertions;
using media_management_app.Models;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Evaluation;

/// <summary>
/// Cross-review 2 (B, hunt side): a torrent the user reset an episode or movie from ("Reset for redownload") is
/// never offered again for that episode or movie; other torrents still are.
/// </summary>
public class ResetDownloadEvaluationTests
{
    private const string ResetHash = "bd9c339c8105439135b8938633db632b4df51fbb";
    private const string OtherHash = "44a6b5a8a4cbb0f104e07332bb7e032328563649";
    private readonly CandidateEvaluationService _sut = new(new SearchTitleResolver());

    private static TorrentSearchResult Row(string name, string hash) =>
        new() { FileName = name, FileUrl = $"magnet:?xt=urn:btih:{hash}&dn=x", FileSize = 1_400_000_000, Seeders = 50 };

    [Fact]
    public void Episode_ResetTorrent_IsRejected_OtherTorrentIsNot()
    {
        var episode = TrackedShowBuilder.Episode(season: 3, episode: 5);
        episode.ResetTorrentHashes = ResetHash;
        var show = TrackedShowBuilder.Show("Rick and Morty", year: 2013);
        var recipe = RecipeBuilder.TvEpisode().Build();

        _sut.EvaluateEpisode(recipe, show, episode, Row("Rick.and.Morty.S03E05.1080p.WEB.x264-GROUP", ResetHash.ToUpperInvariant()))
            .RejectReason.Should().Be(CandidateRejectReason.ResetDownload);
        _sut.EvaluateEpisode(recipe, show, episode, Row("Rick.and.Morty.S03E05.1080p.WEB.x264-OTHER", OtherHash))
            .IsAccepted.Should().BeTrue();
    }

    [Fact]
    public void Movie_ResetTorrent_IsRejected()
    {
        var movie = TrackedShowBuilder.Movie("Coherence", 2014);
        movie.ResetTorrentHashes = ResetHash;

        _sut.EvaluateMovie(RecipeBuilder.Movie().Build(), movie, Row("Coherence.2013.1080p.BluRay.x264-GROUP", ResetHash))
            .RejectReason.Should().Be(CandidateRejectReason.ResetDownload);
    }
}
