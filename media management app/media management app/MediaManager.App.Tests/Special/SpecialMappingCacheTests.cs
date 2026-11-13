using System.IO;
using System.Text.Json;
using MediaManager.App.Tests.Special.Fixtures;
using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Special;

/// <summary>
/// PR-15 (PARSE-11, SPEC-09): the special mapping cache. Nothing is stored before the user accepts; the key covers
/// the TMDB Season 0 list, the pack's files and the prompt version; old entries are used only when they still agree
/// with the rules. Gemini is the scripted fake; nothing here touches the network.
/// </summary>
public sealed class SpecialMappingCacheTests
{
    private static string CachePath(SpecialOrchestratorHost host) =>
        Path.Combine(host.StateFolder, AppConstants.GeminiMappingCacheFileName);

    [Fact]
    public async Task GeminiResult_IsCachedOnlyAfterAccept_ThenReusedWithoutARequest()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.CorrectTwoSeasonPack);

        var first = await ResolveWithCacheAsync(host, pack);

        Assert.Equal(1, host.Gemini.Calls);
        Assert.Equal("fake-model", first.GeminiModel);
        Assert.False(File.Exists(CachePath(host)));

        host.Orchestrator.RememberAccepted(first);
        var second = await ResolveWithCacheAsync(host, pack);

        Assert.True(File.Exists(CachePath(host)));
        Assert.Equal(1, host.Gemini.Calls);
        Assert.True(second.FromCache);
        Assert.Equal("fake-model", second.GeminiModel);
        Assert.Equal(15, second.Items.Count(item => item.Episode is not null));
        Assert.Same(
            pack.Specials.Single(episode => episode.EpisodeNumber == 11),
            second.Items.Single(item => item.RelativePath.EndsWith("S2S03.mkv", StringComparison.Ordinal)).Episode);
    }

    [Fact]
    public async Task TmdbSeasonZeroChanged_IsACacheMiss()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.CorrectTwoSeasonPack);
        host.Orchestrator.RememberAccepted(await ResolveWithCacheAsync(host, pack));

        pack.Specials.Single(episode => episode.EpisodeNumber == 19).Title = "Season 3 Countdown Promo (renamed)";
        await ResolveWithCacheAsync(host, pack);

        Assert.Equal(2, host.Gemini.Calls);
    }

    [Fact]
    public async Task ResultWithDroppedPairs_IsNeverCached()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.DuplicateS00E);

        var result = await ResolveWithCacheAsync(host, pack);
        host.Orchestrator.RememberAccepted(result);

        Assert.Null(result.CacheKey);
        Assert.False(File.Exists(CachePath(host)));
    }

    [Fact]
    public async Task CachedFile_KeepsOnlyWhatLinkingNeeds()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        foreach (var episode in pack.Specials)
        {
            episode.Overview = "Private overview text";
            episode.Thought = "My private note";
        }

        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.CorrectTwoSeasonPack);
        host.Orchestrator.RememberAccepted(await ResolveWithCacheAsync(host, pack));

        var json = await File.ReadAllTextAsync(CachePath(host));
        Assert.DoesNotContain("Private overview text", json);
        Assert.DoesNotContain("My private note", json);
        Assert.Contains("\"Model\": \"fake-model\"", json);
    }

    [Fact]
    public async Task LegacyEntryThatAgreesWithTheRules_IsUsed()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.CorrectTwoSeasonPack);
        WriteLegacyEntry(host, pack, CorrectLegacyItems(pack));

        var result = await ResolveWithCacheAsync(host, pack);

        Assert.Equal(0, host.Gemini.Calls);
        Assert.True(result.FromCache);
        Assert.NotNull(result.CacheKey);
    }

    [Fact]
    public async Task LegacyEntryThatContradictsTheRules_IsACacheMiss()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.CorrectTwoSeasonPack);
        var items = CorrectLegacyItems(pack);
        var s1s03 = items.FindIndex(item => item.RelativePath.EndsWith("S1S03.mkv", StringComparison.Ordinal));
        var s1s04 = items.FindIndex(item => item.RelativePath.EndsWith("S1S04.mkv", StringComparison.Ordinal));
        (items[s1s03], items[s1s04]) = (
            new SpecialMappingItem { RelativePath = items[s1s03].RelativePath, Episode = items[s1s04].Episode, Source = SpecialMappingSource.Gemini },
            new SpecialMappingItem { RelativePath = items[s1s04].RelativePath, Episode = items[s1s03].Episode, Source = SpecialMappingSource.Gemini });
        WriteLegacyEntry(host, pack, items);

        var result = await ResolveWithCacheAsync(host, pack);

        Assert.Equal(1, host.Gemini.Calls);
        Assert.False(result.FromCache);
        Assert.Equal(3, result.Items.Single(item => item.RelativePath.EndsWith("S1S03.mkv", StringComparison.Ordinal)).Episode?.EpisodeNumber);
    }

    // Audit L2-04: the key has no regular-season air dates and no rule version, so a stored entry can hold rule
    // pairs the current rules do not give (a moved season window, an app update). Such an entry is a cache miss.
    // Simulated by swapping two rule-placed files in the stored file, read by a fresh cache.
    [Fact]
    public async Task CachedRulePairsThatDisagreeWithTheCurrentRules_AreACacheMiss()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        string cacheJson;
        using (var writer = new SpecialOrchestratorHost(FakeGeminiResponses.CorrectTwoSeasonPack))
        {
            writer.Orchestrator.RememberAccepted(await ResolveWithCacheAsync(writer, pack));
            cacheJson = await File.ReadAllTextAsync(CachePath(writer));
        }

        var root = System.Text.Json.Nodes.JsonNode.Parse(cacheJson)!;
        var items = root.AsArray().SelectMany(entry => entry!["Result"]!["Items"]!.AsArray()).ToList();
        var s1s03 = items.Single(item => item!["RelativePath"]!.GetValue<string>().EndsWith("S1S03.mkv", StringComparison.Ordinal))!;
        var s1s04 = items.Single(item => item!["RelativePath"]!.GetValue<string>().EndsWith("S1S04.mkv", StringComparison.Ordinal))!;
        var episode03 = s1s03["Episode"]!.DeepClone();
        s1s03["Episode"] = s1s04["Episode"]!.DeepClone();
        s1s04["Episode"] = episode03;

        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.CorrectTwoSeasonPack);
        await File.WriteAllTextAsync(CachePath(host), root.ToJsonString());

        var result = await ResolveWithCacheAsync(host, pack);

        Assert.Equal(1, host.Gemini.Calls);
        Assert.False(result.FromCache);
        Assert.Equal(3, result.Items.Single(item => item.RelativePath.EndsWith("S1S03.mkv", StringComparison.Ordinal)).Episode?.EpisodeNumber);
    }

    // Audit R2-C-04: Retry means the user rejected the cached mapping. If the retry fails (or cannot be cached), the
    // next AI Link must not replay the rejected mapping.
    [Fact]
    public async Task RetryThatFails_ForgetsTheRejectedCachedMapping()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        var geminiDown = false;
        using var host = new SpecialOrchestratorHost(prompt =>
            geminiDown ? throw new InvalidOperationException("Gemini is down.") : FakeGeminiResponses.CorrectTwoSeasonPack(prompt));
        host.Orchestrator.RememberAccepted(await ResolveWithCacheAsync(host, pack));
        geminiDown = true;

        await ResolveWithCacheAsync(host, pack, bypassCache: true);
        var next = await ResolveWithCacheAsync(host, pack);

        Assert.False(next.FromCache);
    }

    // Audit R2-C-03: a cache hit shows the rule rows as the current rules write them, not the stored flags.
    [Fact]
    public async Task CacheHit_RuleRowsCarryTheCurrentTitleFlags()
    {
        var pack = SpecialCases.TwoSeasonBrassPack();
        string cacheJson;
        using (var writer = new SpecialOrchestratorHost(FakeGeminiResponses.CorrectTwoSeasonPack))
        {
            writer.Orchestrator.RememberAccepted(await ResolveWithCacheAsync(writer, pack));
            cacheJson = await File.ReadAllTextAsync(CachePath(writer));
        }

        var root = System.Text.Json.Nodes.JsonNode.Parse(cacheJson)!;
        var ruleRow = root.AsArray()
            .SelectMany(entry => entry!["Result"]!["Items"]!.AsArray())
            .First(item => item!["RelativePath"]!.GetValue<string>().EndsWith("S1S03.mkv", StringComparison.Ordinal))!;
        ruleRow["TitleConfirmed"] = true;
        ruleRow["Reason"] = "stale reason from an older app";

        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.CorrectTwoSeasonPack);
        await File.WriteAllTextAsync(CachePath(host), root.ToJsonString());

        var result = await ResolveWithCacheAsync(host, pack);

        Assert.True(result.FromCache);
        var row = result.Items.Single(item => item.RelativePath.EndsWith("S1S03.mkv", StringComparison.Ordinal));
        Assert.False(row.TitleConfirmed);
        Assert.NotEqual("stale reason from an older app", row.Reason);
    }

    private static Task<SpecialMappingResult> ResolveWithCacheAsync(SpecialOrchestratorHost host, SpecialCase pack, bool bypassCache = false)
    {
        var tree = PackFolderTreeAnalyzer.Analyze(pack.Files.Select(file => file.RelativePath).ToList());
        return host.Orchestrator.ResolveAsync(
            pack.Files,
            tree,
            pack.Episodes,
            pack.ShowTitle,
            pack.TmdbId,
            "hash-" + pack.Name,
            progress: null,
            useGemini: true,
            bypassCache: bypassCache);
    }

    /// <summary>What a pre-PR-15 cache held for this pack: every special as a Gemini pair, all correct.</summary>
    private static List<SpecialMappingItem> CorrectLegacyItems(SpecialCase pack) =>
        pack.ExpectedS00E
            .Select(pair => new SpecialMappingItem
            {
                RelativePath = pair.Key,
                Episode = pack.Specials.Single(episode => episode.EpisodeNumber == pair.Value),
                Source = SpecialMappingSource.Gemini,
                Reason = "Gemini mapped."
            })
            .ToList();

    private static void WriteLegacyEntry(SpecialOrchestratorHost host, SpecialCase pack, IReadOnlyList<SpecialMappingItem> items)
    {
        var legacy = new[]
        {
            new
            {
                TorrentHash = "hash-" + pack.Name,
                pack.TmdbId,
                CachedUtc = new DateTime(2026, 6, 23, 0, 0, 0, DateTimeKind.Utc),
                Result = new SpecialMappingResult { Items = items, UsedGemini = true }
            }
        };
        File.WriteAllText(CachePath(host), JsonSerializer.Serialize(legacy));
    }
}
