using MediaManager.App.Tests.Special.Fixtures;
using media_management_app.Models;

namespace GeminiSpecialEval.Cases;

/// <summary>
/// One pack with known answers: the show's TMDB episodes, the pack's file list and the S00 episode each special
/// file really is (null = the file is not a TMDB special, so the right answer is to leave it out).
/// </summary>
internal sealed record EvalCase(
    string Name,
    string Source,
    string ShowTitle,
    int TmdbId,
    IReadOnlyList<TrackedEpisode> Episodes,
    IReadOnlyList<(string RelativePath, string FileName)> Files,
    IReadOnlyDictionary<string, int?> ExpectedS00E,
    string Note,
    IReadOnlyList<string> Tags)
{
    /// <summary>Synthetic cases live in the repo; private ones only in the data folder and are never printed to the repo.</summary>
    public bool IsPrivate => Source == CaseSources.Private;

    public static EvalCase FromFixture(SpecialCase fixture, string source, params string[] tags) =>
        new(
            fixture.Name,
            source,
            fixture.ShowTitle,
            fixture.TmdbId,
            fixture.Episodes,
            fixture.Files,
            fixture.ExpectedS00E,
            fixture.Note,
            tags);

    public override string ToString() => Name;
}

internal static class CaseSources
{
    /// <summary>The test project's fixtures (<c>SpecialCases</c>).</summary>
    public const string Fixtures = "fixtures";

    /// <summary>Synthetic cases written for this harness (<see cref="EvalOnlyCases"/>).</summary>
    public const string Eval = "eval";

    /// <summary>JSON cases from the data folder (real packs); never committed.</summary>
    public const string Private = "private";
}
