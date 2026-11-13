using System.Text.Json;
using MediaManager.App.Tests.Special.Fixtures;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Special;

/// <summary>
/// PR-14 (PARSE-16, PARSE-21, SPEC-10): Gemini only sees the specials the rules left open, and its answer goes
/// through hard checks. Gemini is the scripted fake; nothing here touches the network.
/// </summary>
public sealed class GeminiPayloadV2Tests
{
    private static readonly SpecialCase Pack = SpecialCases.TwoSeasonBrassPack();

    [Fact]
    public async Task Payload_SendsOnlyTheFilesTheRulesLeftOpen_WithFixedAndFreeEpisodes()
    {
        string? sent = null;
        using var host = new SpecialOrchestratorHost(json =>
        {
            sent = json;
            return FakeGeminiResponses.CorrectTwoSeasonPack(json);
        });

        await host.ResolveAsync(Pack);

        Assert.NotNull(sent);
        using var document = JsonDocument.Parse(sent!);
        var root = document.RootElement;
        Assert.Equal(3, root.GetProperty("v").GetInt32());
        Assert.False(root.TryGetProperty("p", out _));

        var context = root.GetProperty("ctx");
        var files = context.GetProperty("c").EnumerateArray().ToList();
        Assert.Equal(7, files.Count);
        Assert.All(files, file => Assert.Contains("S2S", file[1].GetString()));
        Assert.All(files, file => Assert.Equal("Specials", file[0].GetString()));
        Assert.Equal(
            Enumerable.Range(1, 7),
            files.Select(file => file[3].GetInt32()));

        var fixedEpisodes = context.GetProperty("fx").EnumerateArray().Select(pair => pair[0].GetInt32()).ToList();
        Assert.Equal(Enumerable.Range(1, 8), fixedEpisodes);
        // v3: each fixed pair carries what the order rule needs (parentSeason, localIndex, patternCode).
        var firstFixed = context.GetProperty("fx")[0];
        Assert.Equal(5, firstFixed.GetArrayLength());
        Assert.Equal(1, firstFixed[2].GetInt32());
        Assert.Equal(1, firstFixed[3].GetInt32());
        Assert.Equal("s", firstFixed[4].GetString());
        var free = context.GetProperty("free").EnumerateArray().Select(item => item.GetInt32()).ToList();
        Assert.DoesNotContain(free, episode => episode <= 8);
        Assert.Contains(9, free);

        Assert.True(context.GetProperty("sw").GetArrayLength() >= 2);
        Assert.DoesNotContain(SpecialCases.BrassS2, sent);
    }

    [Fact]
    public void Payload_KeepsNonAsciiTitlesReadable()
    {
        var request = new SpecialMappingAIRequest
        {
            Context = new SpecialMappingContext
            {
                ShowTitle = "響け！ユーフォニアム",
                Candidates = [Candidate("Show/Specials/[Grp] 響け - 01.mkv", SpecialMappingNamingPattern.Opaque, 1, 1)],
                TmdbSpecials = [(1, "2015-06-17", "第1話 リハーサル")]
            },
            GeminiCandidateIndices = [0]
        };

        var json = SpecialMappingCompactJson.SerializeRequest(request);

        Assert.Contains("響け！ユーフォニアム", json);
        Assert.Contains("リハーサル", json);
        Assert.DoesNotContain("\\u", json);
    }

    [Theory]
    [InlineData("archive-s1-ovas")]
    [InlineData("club-ova-per-season")]
    public async Task Resolve_RulesPlaceEverySpecial_GeminiIsNotCalled(string name)
    {
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("Gemini must not be called."));

        var result = await host.ResolveAsync(SpecialCases.ByName(name));

        Assert.Equal(0, host.Gemini.Calls);
        Assert.False(result.UsedGemini);
        Assert.All(result.Items, item => Assert.NotNull(item.Episode));
    }

    [Fact]
    public async Task Resolve_GeminiAgreesWithSuggestion_RulePairsStayRulePairs()
    {
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.CorrectTwoSeasonPack);

        var result = await host.ResolveAsync(Pack);

        var s2s03 = result.Items.Single(item => item.RelativePath.EndsWith("S2S03.mkv", StringComparison.Ordinal));
        Assert.Equal(11, s2s03.Episode?.EpisodeNumber);
        Assert.Equal(SpecialMappingSource.Gemini, s2s03.Source);
        Assert.True(s2s03.MatchesSuggestion);
        Assert.Contains("agrees", s2s03.Reason, StringComparison.Ordinal);

        var s1s03 = result.Items.Single(item => item.RelativePath.EndsWith("S1S03.mkv", StringComparison.Ordinal));
        Assert.Equal(SpecialMappingSource.Rule, s1s03.Source);
        Assert.Equal(3, s1s03.Episode?.EpisodeNumber);
        Assert.Equal(15, result.Items.Count(item => item.Episode is not null));
    }

    [Fact]
    public async Task Resolve_GeminiTakesAnEpisodeTheRulesPlaced_PairIsIgnored()
    {
        using var host = new SpecialOrchestratorHost(json => FakeGeminiResponses.ByFileName(json, fileName =>
            fileName.Contains("S2S01", StringComparison.Ordinal) ? 3 : null));

        var result = await host.ResolveAsync(Pack);

        Assert.Null(result.Items.Single(item => item.RelativePath.EndsWith("S2S01.mkv", StringComparison.Ordinal)).Episode);
        Assert.Equal(3, result.Items.Single(item => item.RelativePath.EndsWith("S1S03.mkv", StringComparison.Ordinal)).Episode?.EpisodeNumber);
        Assert.Contains(result.Warnings, warning => warning.Contains("already placed", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_FileNameSaysS00E_GeminiGivesAnotherNumber_PairIsDropped()
    {
        var (request, episodes) = ThreeSeasonRequest(Candidate("Show/Specials/Show - S00E40.mkv", SpecialMappingNamingPattern.StandardS00E, 1, 40));
        var result = GeminiResult(request, episodes[2]);

        var violations = SpecialMappingValidator.ValidateGeminiPairs(result, request);

        var violation = Assert.Single(violations);
        Assert.Contains("S00E40", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_EpisodeFromAnotherSeasonsWindow_PairIsDropped()
    {
        var (request, episodes) = ThreeSeasonRequest(Candidate("Show/S1/Specials/Show - S1S01.mkv", SpecialMappingNamingPattern.SnSnn, 1, 1));

        var violations = SpecialMappingValidator.ValidateGeminiPairs(GeminiResult(request, episodes[4]), request);

        Assert.Contains("season 3", Assert.Single(violations).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_FirstEntryOfTheNextWindow_IsAllowed()
    {
        var (request, episodes) = ThreeSeasonRequest(Candidate("Show/S1/Specials/Show - S1S01.mkv", SpecialMappingNamingPattern.SnSnn, 1, 1));

        Assert.Empty(SpecialMappingValidator.ValidateGeminiPairs(GeminiResult(request, episodes[2]), request));
    }

    // Audit L2-03: a root OVA folder (no parent season) keeps its order too: "OVA 03" never gets an earlier episode
    // than "OVA 02".
    [Fact]
    public void Validator_RootOvaFolderOutOfOrder_OnePairIsDropped()
    {
        var episodes = SixSpecials();
        var request = OrderRequest(
            Candidate("Show/OVA/Show - OVA - 01.mkv", SpecialMappingNamingPattern.OvaDash, null, 1),
            Candidate("Show/OVA/Show - OVA - 02.mkv", SpecialMappingNamingPattern.OvaDash, null, 2),
            Candidate("Show/OVA/Show - OVA - 03.mkv", SpecialMappingNamingPattern.OvaDash, null, 3));
        var result = Pairs(request,
            (0, episodes[4], SpecialMappingSource.Gemini),
            (1, episodes[6], SpecialMappingSource.Gemini),
            (2, episodes[5], SpecialMappingSource.Gemini));

        var violations = SpecialMappingValidator.ValidateGeminiPairs(result, request);

        Assert.Contains("order", Assert.Single(violations).Message, StringComparison.Ordinal);
    }

    // Audit L2-03: a Gemini pair must keep the order against the pairs the rules placed in the same group; only
    // the Gemini pair is dropped, never the rule pair.
    [Fact]
    public void Validator_GeminiPairOutOfOrderAgainstARulePair_TheGeminiPairIsDropped()
    {
        var episodes = SixSpecials();
        var request = OrderRequest(
            Candidate("Show/S1/Specials/Show - S1S01.mkv", SpecialMappingNamingPattern.SnSnn, 1, 1),
            Candidate("Show/S1/Specials/Show - S1S02.mkv", SpecialMappingNamingPattern.SnSnn, 1, 2),
            Candidate("Show/S1/Specials/Show - S1S03.mkv", SpecialMappingNamingPattern.SnSnn, 1, 3));
        var result = Pairs(request,
            (1, episodes[2], SpecialMappingSource.Rule),
            (0, episodes[3], SpecialMappingSource.Gemini),
            (2, episodes[4], SpecialMappingSource.Gemini));

        var violation = Assert.Single(SpecialMappingValidator.ValidateGeminiPairs(result, request));

        Assert.EndsWith("S1S01.mkv", violation.RelativePath, StringComparison.Ordinal);
    }

    private static List<TrackedEpisode> SixSpecials() =>
        Enumerable.Range(0, 7)
            .Select(number => new TrackedEpisode { SeasonNumber = 0, EpisodeNumber = number, Title = $"Special {number}" })
            .ToList();

    private static SpecialMappingAIRequest OrderRequest(params SpecialMappingCandidate[] candidates) =>
        new()
        {
            Context = new SpecialMappingContext { ShowTitle = "Show", Candidates = candidates },
            GeminiCandidateIndices = Enumerable.Range(0, candidates.Length).ToList()
        };

    private static SpecialMappingResult Pairs(
        SpecialMappingAIRequest request,
        params (int Candidate, TrackedEpisode Episode, SpecialMappingSource Source)[] pairs) =>
        new()
        {
            Items = pairs
                .Select(pair => new SpecialMappingItem
                {
                    RelativePath = request.Context.Candidates[pair.Candidate].RelativePath,
                    Episode = pair.Episode,
                    Source = pair.Source
                })
                .ToList()
        };

    private static SpecialMappingCandidate Candidate(string path, SpecialMappingNamingPattern pattern, int? parentSeason, int localIndex) =>
        new()
        {
            RelativePath = path,
            FileName = path.Split('/').Last(),
            ParentSeason = parentSeason,
            LocalIndex = localIndex,
            NamingPattern = pattern
        };

    /// <summary>Six S00 entries, two per season window (seasons 1, 2, 3).</summary>
    private static (SpecialMappingAIRequest Request, IReadOnlyList<TrackedEpisode> Episodes) ThreeSeasonRequest(SpecialMappingCandidate candidate)
    {
        var episodes = Enumerable.Range(1, 6)
            .Select(number => new TrackedEpisode
            {
                SeasonNumber = 0,
                EpisodeNumber = number,
                Title = $"Special {number}",
                AirDate = new DateTime(2020 + (number - 1) / 2, 6, 1)
            })
            .ToList();
        var blocks = new List<SpecialSeasonBlock>
        {
            new(1, new DateTime(2020, 1, 1), [episodes[0], episodes[1]]),
            new(2, new DateTime(2021, 1, 1), [episodes[2], episodes[3]]),
            new(3, new DateTime(2022, 1, 1), [episodes[4], episodes[5]])
        };
        var request = new SpecialMappingAIRequest
        {
            Context = new SpecialMappingContext { ShowTitle = "Show", Candidates = [candidate] },
            GeminiCandidateIndices = [0],
            SeasonBlocks = blocks
        };
        return (request, episodes);
    }

    private static SpecialMappingResult GeminiResult(SpecialMappingAIRequest request, TrackedEpisode episode) =>
        new()
        {
            Items =
            [
                new SpecialMappingItem
                {
                    RelativePath = request.Context.Candidates[0].RelativePath,
                    Episode = episode,
                    Source = SpecialMappingSource.Gemini
                }
            ]
        };
}
