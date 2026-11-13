using GeminiSpecialEval.Cases;
using GeminiSpecialEval.Recording;
using GeminiSpecialEval.Running;
using media_management_app.Services;

namespace GeminiSpecialEval.Scoring;

/// <summary>What happened to one special file Gemini was asked about.</summary>
internal enum GeminiOutcome
{
    /// <summary>Linked to the right S00 episode.</summary>
    Correct,

    /// <summary>Linked to another S00 episode (or a non-special was linked): a silent wrong link.</summary>
    Wrong,

    /// <summary>Gemini answered right, but the app's checks dropped the pair (a false alarm of the validator).</summary>
    DroppedRight,

    /// <summary>Gemini answered wrong and the app's checks dropped the pair (the validator saved a wrong link).</summary>
    DroppedWrong,

    /// <summary>Gemini gave no answer for a real special (left for the review).</summary>
    LeftOut,

    /// <summary>Not a TMDB special, and Gemini rightly left it out.</summary>
    AbstainCorrect,

    /// <summary>No usable answer at all (HTTP error, timeout, unparseable reply): the app fell back to its rules.</summary>
    Error
}

/// <summary>What the rules (no Gemini) did with one special file.</summary>
internal enum RuleOutcome
{
    Correct,
    Wrong,
    Unmapped,

    /// <summary>The file is a special but the builder never treated it as a candidate.</summary>
    NotDetected,

    /// <summary>Sent to Gemini (scored per sample instead).</summary>
    SentToGemini
}

internal sealed record GeminiFileScore(
    string RelativePath,
    int? Expected,
    IReadOnlyList<int> Raw,
    int? Final,
    GeminiOutcome Outcome,
    string? Reason)
{
    public string FileName => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];
}

internal sealed record RuleFileScore(string RelativePath, int? Expected, int? Final, RuleOutcome Outcome, string? Reason)
{
    public string FileName => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];
}

internal sealed record SampleScore(
    SampleRecord Record,
    EvalCase Case,
    IReadOnlyList<GeminiFileScore> Files,
    int RawPairs,
    int RawPairsRight,
    bool Stale,
    string? ReplayNote);

internal static class Scorer
{
    /// <summary>The rule tier of a case: identical for every model and prompt.</summary>
    public static IReadOnlyList<RuleFileScore> ScoreRules(CaseProbeResult probe)
    {
        var request = probe.Request;
        var rules = PackSpecialPayloadBuilder.ApplyProposals(request, probe.Specials, media_management_app.Models.SpecialMappingSource.Rule);
        var checkedRules = SpecialMappingValidator.RemoveViolations(rules, probe.Specials);
        var sent = request.GeminiCandidateIndices
            .Select(index => request.Context.Candidates[index].RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidatePaths = request.Context.Candidates
            .Select(candidate => candidate.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scores = new List<RuleFileScore>();
        foreach (var candidate in request.Context.Candidates)
        {
            var known = probe.Case.ExpectedS00E.TryGetValue(candidate.RelativePath, out var expected);
            if (sent.Contains(candidate.RelativePath))
            {
                scores.Add(new RuleFileScore(candidate.RelativePath, known ? expected : null, null, RuleOutcome.SentToGemini, null));
                continue;
            }

            var item = checkedRules.ByPath.GetValueOrDefault(candidate.RelativePath);
            var final = item?.Episode?.EpisodeNumber;
            var outcome = final is null
                ? RuleOutcome.Unmapped
                : final == expected ? RuleOutcome.Correct : RuleOutcome.Wrong;
            scores.Add(new RuleFileScore(candidate.RelativePath, known ? expected : null, final, outcome, item?.Reason));
        }

        foreach (var (path, expected) in probe.Case.ExpectedS00E.Where(pair => pair.Value is not null && !candidatePaths.Contains(pair.Key)))
        {
            scores.Add(new RuleFileScore(path, expected, null, RuleOutcome.NotDetected, "not a special/OVA candidate for the builder"));
        }

        return scores;
    }

    /// <summary>Scores the Gemini files of one sample from the app's final result and Gemini's raw answer.</summary>
    public static SampleScore ScoreSample(
        SampleRecord record,
        CaseProbeResult probe,
        CaseRun run,
        bool stale,
        string? replayNote)
    {
        var request = probe.Request;
        var sentPaths = request.GeminiCandidateIndices
            .Select(index => request.Context.Candidates[index].RelativePath)
            .ToList();

        // Gemini's own pairs, before any check: candidateIndex k is the k-th file sent.
        var raw = sentPaths.ToDictionary(path => path, _ => new List<int>(), StringComparer.OrdinalIgnoreCase);
        var parsed = false;
        if (!string.IsNullOrWhiteSpace(run.Client.ResponseText) &&
            SpecialMappingCompactJson.TryParseResponse(run.Client.ResponseText, out var response, out _) &&
            response is not null)
        {
            parsed = true;
            foreach (var (index, episode) in response.Mappings)
            {
                if (index >= 0 && index < sentPaths.Count)
                {
                    raw[sentPaths[index]].Add(episode);
                }
            }
        }

        var failed = !parsed;
        var files = new List<GeminiFileScore>();
        var rawPairs = 0;
        var rawRight = 0;
        foreach (var path in sentPaths)
        {
            var known = probe.Case.ExpectedS00E.TryGetValue(path, out var expectedValue);
            int? expected = known ? expectedValue : null;
            var item = run.Result.ByPath.GetValueOrDefault(path);
            var final = item?.Episode?.EpisodeNumber;
            var answers = raw[path];
            rawPairs += answers.Count;
            rawRight += answers.Count(answer => answer == expected);

            GeminiOutcome outcome;
            if (failed)
            {
                outcome = GeminiOutcome.Error;
            }
            else if (final is not null)
            {
                outcome = final == expected ? GeminiOutcome.Correct : GeminiOutcome.Wrong;
            }
            else if (answers.Count == 0)
            {
                outcome = expected is null ? GeminiOutcome.AbstainCorrect : GeminiOutcome.LeftOut;
            }
            else
            {
                outcome = expected is not null && answers.Count == 1 && answers[0] == expected
                    ? GeminiOutcome.DroppedRight
                    : GeminiOutcome.DroppedWrong;
            }

            files.Add(new GeminiFileScore(path, expected, answers, final, outcome, item?.Reason));
        }

        return new SampleScore(record, probe.Case, files, rawPairs, rawRight, stale, replayNote);
    }
}
