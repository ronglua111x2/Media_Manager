using System.IO;
using MediaManager.App.Tests.Special.Fixtures;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.App.Tests.Special;

/// <summary>
/// A failure in the special/OVA step must not cost the regular episodes of the pack (PARSE-10, report 4a/10.3 point 6).
/// Gemini is always the scripted fake; nothing here touches the network.
/// </summary>
public class SpecialMappingFailureTests
{
    private static readonly SpecialCase Pack = SpecialCases.TwoSeasonBrassPack();

    [Fact]
    public async Task Resolve_GeminiRequestFails_FallsBackToRulesWithWarning()
    {
        using var host = new SpecialOrchestratorHost(_ =>
            throw new InvalidOperationException("Gemini request failed for all configured models."));

        var result = await host.ResolveAsync(Pack);

        Assert.False(result.UsedGemini);
        Assert.All(result.Items, item => Assert.Equal(SpecialMappingSource.Rule, item.Source));
        Assert.Contains(result.Warnings, warning => warning.Contains("Gemini", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(FakeGeminiResponses.TruncatedInMappings)]
    [InlineData(FakeGeminiResponses.MissingMappingArray)]
    public async Task Resolve_GeminiResponseUnreadable_FallsBackToRules(string response)
    {
        using var host = new SpecialOrchestratorHost(_ => response);

        var result = await host.ResolveAsync(Pack);

        Assert.False(result.UsedGemini);
        Assert.Contains(result.Warnings, warning => warning.Contains("Gemini", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Resolve_GeminiRepeatsCandidateIndex_LeavesThatFileUnmappedWithWarning()
    {
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.DuplicateCandidateIndex);

        var result = await host.ResolveAsync(Pack);

        var first = result.Items.Where(item => item.RelativePath.EndsWith("S2S01.mkv", StringComparison.Ordinal)).ToList();
        Assert.Single(first);
        Assert.Null(first[0].Episode);
        Assert.Equal(10, result.ByPath[Pack.Files.Single(file => file.FileName.EndsWith("S2S02.mkv")).RelativePath].Episode?.EpisodeNumber);
        Assert.Contains(result.Warnings, warning => warning.Contains("S2S01", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Resolve_GeminiRepeatsS00Episode_DropsBothPairsWithWarning()
    {
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.DuplicateS00E);

        var result = await host.ResolveAsync(Pack);

        Assert.DoesNotContain(result.Items, item => item.Episode?.EpisodeNumber == 9);
        Assert.Contains(result.Warnings, warning => warning.Contains("S00E09", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(11, result.Items.Single(item => item.RelativePath.EndsWith("S2S03.mkv", StringComparison.Ordinal)).Episode?.EpisodeNumber);
    }

    [Fact]
    public void Validator_DuplicateEpisode_ReportsInsteadOfThrowing()
    {
        var specials = Pack.Specials;
        var result = new SpecialMappingResult
        {
            Items =
            [
                new SpecialMappingItem { RelativePath = "a.mkv", Episode = specials[0], Source = SpecialMappingSource.Gemini },
                new SpecialMappingItem { RelativePath = "b.mkv", Episode = specials[0], Source = SpecialMappingSource.Gemini }
            ]
        };

        var error = Record.Exception(() => SpecialMappingValidator.Validate(result, specials));

        Assert.Null(error);
    }

    [Fact]
    public async Task Prepare_SpecialStepThrows_RegularEpisodesStayLinkableAndReviewIsRequired()
    {
        var service = SpecialPackPrepareHost.Create(
            Pack,
            SpecialPackPrepareHost.ThrowingOrchestrator(new InvalidOperationException("Duplicate special mapping for S00E01.")));

        var preview = await service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, useGeminiForSpecials: true);

        Assert.Equal(26, preview.Inventory.Files.Count(file => file.Classification == PackFileClassification.RegularEpisode));
        Assert.True(preview.RequiresReview);
        Assert.Contains(preview.SpecialMappings.Warnings, warning => warning.Contains("S00E01", StringComparison.Ordinal));
        Assert.DoesNotContain(
            preview.Inventory.Files,
            file => file.Classification == PackFileClassification.RegularEpisode && Pack.ExpectedS00E.ContainsKey(file.RelativePath));
    }

    [Fact]
    public async Task Prepare_UserCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = SpecialPackPrepareHost.Create(
            Pack,
            SpecialPackPrepareHost.ThrowingOrchestrator(new OperationCanceledException(cancellation.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Prepare_GeminiAnswered_RequiresReview()
    {
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.RecordedSevenOfFifteenWrong);
        var service = SpecialPackPrepareHost.Create(Pack, host.Orchestrator);

        var preview = await service.PrepareSeasonPackLinkAsync(SpecialPackPrepareHost.ShowId, 1, useGeminiForSpecials: true);

        Assert.True(preview.RequiresReview);
        Assert.Equal(26, preview.RegularEpisodeCount);
    }

    [Fact]
    public async Task Resolve_GeminiRepeatsS00Episode_ResultIsNotCached()
    {
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.DuplicateS00E);

        var result = await host.ResolveAsync(Pack);

        Assert.True(result.RequiresReview);
        Assert.False(File.Exists(Path.Combine(host.StateFolder, media_management_app.Common.AppConstants.GeminiMappingCacheFileName)));
    }

    [Fact]
    public async Task Resolve_GeminiFails_ResultAsksForReviewAndNamesTheReason()
    {
        using var host = new SpecialOrchestratorHost(_ => throw new InvalidOperationException("HTTP 429 for all models."));

        var result = await host.ResolveAsync(Pack);

        Assert.True(result.RequiresReview);
        Assert.Contains("HTTP 429", result.FallbackReason);
    }

    /// <summary>Note R5: a response cut by the token limit is flagged even when what arrived still parses.</summary>
    [Fact]
    public async Task Resolve_GeminiStoppedEarly_WarnsThatSpecialsMayBeMissing()
    {
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.CorrectTwoSeasonPack, finishReason: "MAX_TOKENS");

        var result = await host.ResolveAsync(Pack);

        Assert.True(result.UsedGemini);
        Assert.Contains(result.Warnings, warning => warning.Contains("MAX_TOKENS", StringComparison.Ordinal));
    }

    /// <summary>Note R10: a Gemini warning that merely contains "vs" must not hide the count mismatch warning.</summary>
    [Fact]
    public async Task Resolve_GeminiWarningContainsVs_CountMismatchWarningKept()
    {
        using var host = new SpecialOrchestratorHost(json =>
            FakeGeminiResponses.CorrectTwoSeasonPack(json).Replace("\"w\":[]", "\"w\":[\"TVs and BDs differ\"]", StringComparison.Ordinal));

        var result = await host.ResolveAsync(Pack);

        Assert.Contains("15 torrent special file(s) vs 32 TMDB S00 episode(s).", result.Warnings);
    }

    [Fact]
    public async Task Resolve_GeminiIndexOutOfRange_IsReported()
    {
        using var host = new SpecialOrchestratorHost(_ => "{\"m\":[[5000,1],[-1,2]],\"w\":[]}");

        var result = await host.ResolveAsync(Pack);

        Assert.Contains(result.Warnings, warning => warning.Contains("5000", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("-1", StringComparison.Ordinal));
    }

    /// <summary>
    /// PARSE-16: the recorded wrong answer (S1S07 to S00E01) cannot happen any more, because Gemini no longer sees
    /// season 1 (the rules place it); the same kind of answer on season 2 is caught by the season order check.
    /// </summary>
    [Fact]
    public async Task Resolve_RecordedWrongAnswer_SeasonOneStaysWithTheRules()
    {
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.RecordedSevenOfFifteenWrong);

        var result = await host.ResolveAsync(Pack);

        var s1s07 = result.Items.Single(item => item.RelativePath.EndsWith("S1S07.mkv", StringComparison.Ordinal));
        Assert.Equal(7, s1s07.Episode?.EpisodeNumber);
        Assert.Equal(SpecialMappingSource.Rule, s1s07.Source);
    }

    [Fact]
    public async Task Resolve_GeminiBreaksSeasonOrder_PairIsDroppedForReview()
    {
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.SeasonTwoBreaksOrder);

        var result = await host.ResolveAsync(Pack);

        Assert.Null(result.Items.Single(item => item.RelativePath.EndsWith("S2S07.mkv", StringComparison.Ordinal)).Episode);
        Assert.Equal(15, result.Items.Single(item => item.RelativePath.EndsWith("S2S06.mkv", StringComparison.Ordinal)).Episode?.EpisodeNumber);
        Assert.True(result.RequiresReview);
        Assert.Contains(result.Warnings, warning => warning.Contains("order", StringComparison.Ordinal));
    }
}
