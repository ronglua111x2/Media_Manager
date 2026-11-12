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

    [Fact(Skip = "PARSE-10")]
    public async Task Resolve_GeminiRequestFails_FallsBackToRulesWithWarning()
    {
        using var host = new SpecialOrchestratorHost(_ =>
            throw new InvalidOperationException("Gemini request failed for all configured models."));

        var result = await host.ResolveAsync(Pack);

        Assert.False(result.UsedGemini);
        Assert.All(result.Items, item => Assert.Equal(SpecialMappingSource.Rule, item.Source));
        Assert.Contains(result.Warnings, warning => warning.Contains("Gemini", StringComparison.OrdinalIgnoreCase));
    }

    [Theory(Skip = "PARSE-10")]
    [InlineData(FakeGeminiResponses.TruncatedInMappings)]
    [InlineData(FakeGeminiResponses.MissingMappingArray)]
    public async Task Resolve_GeminiResponseUnreadable_FallsBackToRules(string response)
    {
        using var host = new SpecialOrchestratorHost(_ => response);

        var result = await host.ResolveAsync(Pack);

        Assert.False(result.UsedGemini);
        Assert.Contains(result.Warnings, warning => warning.Contains("Gemini", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "PARSE-10")]
    public async Task Resolve_GeminiRepeatsCandidateIndex_LeavesThatFileUnmappedWithWarning()
    {
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.DuplicateCandidateIndex);

        var result = await host.ResolveAsync(Pack);

        var first = result.Items.Where(item => item.RelativePath.EndsWith("S1S01.mkv", StringComparison.Ordinal)).ToList();
        Assert.Single(first);
        Assert.Null(first[0].Episode);
        Assert.Equal(2, result.ByPath[Pack.Files.Single(file => file.FileName.EndsWith("S1S02.mkv")).RelativePath].Episode?.EpisodeNumber);
        Assert.Contains(result.Warnings, warning => warning.Contains("S1S01", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "PARSE-10")]
    public async Task Resolve_GeminiRepeatsS00Episode_DropsBothPairsWithWarning()
    {
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.DuplicateS00E);

        var result = await host.ResolveAsync(Pack);

        Assert.DoesNotContain(result.Items, item => item.Episode?.EpisodeNumber == 1);
        Assert.Contains(result.Warnings, warning => warning.Contains("S00E01", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, result.Items.Single(item => item.RelativePath.EndsWith("S1S03.mkv", StringComparison.Ordinal)).Episode?.EpisodeNumber);
    }

    [Fact(Skip = "PARSE-10")]
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

    [Fact(Skip = "PARSE-10")]
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

    /// <summary>
    /// The recorded wrong answer keeps the pairs one-to-one, so only a block-order check can catch it (PR-14, not in this change).
    /// </summary>
    [Fact(Skip = "PARSE-16")]
    public async Task Resolve_GeminiBreaksBlockOrder_PairIsDroppedForReview()
    {
        using var host = new SpecialOrchestratorHost(FakeGeminiResponses.RecordedSevenOfFifteenWrong);

        var result = await host.ResolveAsync(Pack);

        Assert.Null(result.Items.Single(item => item.RelativePath.EndsWith("S1S07.mkv", StringComparison.Ordinal)).Episode);
    }
}
