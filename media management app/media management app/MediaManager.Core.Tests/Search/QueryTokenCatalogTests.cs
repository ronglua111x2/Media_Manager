using FluentAssertions;
using media_management_app.Common;
using media_management_app.Services;
using MediaManager.Core.Tests.Fixtures;

namespace MediaManager.Core.Tests.Search;

public class QueryTokenCatalogTests
{
    [Fact]
    public void Parse_ExtractsPaddedSeasonAsSingleToken()
    {
        var spans = QueryTokenCatalog.Parse("{title} S{season:00}E{episode:00}");

        spans.Select(span => span.Id).Should().Equal("title", "season:00", "episode:00");
        spans[1].Start.Should().Be("{title} S".Length);
    }

    [Fact]
    public void Validate_AcceptsTvEpisodeTokens()
    {
        var result = QueryTokenCatalog.Validate(
            "{title} S{season:00}E{episode:00} {quality}",
            MediaKind.TvEpisode);

        result.IsAccepted.Should().BeTrue();
        result.RejectedTokenId.Should().BeNull();
    }

    [Fact]
    public void Validate_RejectsRetiredAudioToken()
    {
        var result = QueryTokenCatalog.Validate("{title} {audio}", MediaKind.TvEpisode);

        result.IsAccepted.Should().BeFalse();
        result.RejectedTokenId.Should().Be(QueryTokenCatalog.AudioId);
        result.Reason.Should().Contain("removed");
    }

    [Fact]
    public void Validate_RejectsUnknownToken()
    {
        var result = QueryTokenCatalog.Validate("{title} {foo}", MediaKind.Movie);

        result.IsAccepted.Should().BeFalse();
        result.RejectedTokenId.Should().Be("foo");
        result.Reason.Should().Contain("unknown");
    }

    [Fact]
    public void Validate_RejectsEpisodeTokenOnMovie()
    {
        var result = QueryTokenCatalog.Validate("{title} {episode:00}", MediaKind.Movie);

        result.IsAccepted.Should().BeFalse();
        result.RejectedTokenId.Should().Be(QueryTokenCatalog.EpisodePaddedId);
    }

    [Fact]
    public void NormalizeTemplates_DropsBlanksAndCapsAtTen()
    {
        var input = Enumerable.Range(1, 12).Select(i => $"{{title}} {i}").Append("  ").Append("").ToList();
        input.Insert(0, "   ");

        var normalized = QueryTokenCatalog.NormalizeTemplates(input);

        normalized.Should().HaveCount(QueryTokenCatalog.MaxTemplates);
        normalized[0].Should().Be("{title} 1");
        normalized.Should().NotContain(string.Empty);
    }

    [Fact]
    public void SplitDisplayParts_HighlightsValidAndInvalidTokens()
    {
        var parts = QueryTokenCatalog.SplitDisplayParts("{title} S{season:00} {audio} extra", MediaKind.TvEpisode);

        parts.Should().HaveCount(6);
        parts[0].Text.Should().Be("{title}");
        parts[0].Kind.Should().Be(QueryTemplatePartKind.ValidToken);
        parts[1].Text.Should().Be(" S");
        parts[1].Kind.Should().Be(QueryTemplatePartKind.Literal);
        parts[2].Text.Should().Be("{season:00}");
        parts[2].Kind.Should().Be(QueryTemplatePartKind.ValidToken);
        parts[3].Text.Should().Be(" ");
        parts[4].Text.Should().Be("{audio}");
        parts[4].Kind.Should().Be(QueryTemplatePartKind.InvalidToken);
        parts[0].TokenId.Should().Be(QueryTokenCatalog.TitleId);
        parts[0].DisplayBrushKey.Should().Be(QueryTokenCatalog.TitleBrushKey);
        parts[2].DisplayBrushKey.Should().Be(QueryTokenCatalog.SeasonBrushKey);
        parts[4].DisplayBrushKey.Should().Be(QueryTokenCatalog.InvalidBrushKey);
        parts[5].Text.Should().Be(" extra");
        parts[5].Kind.Should().Be(QueryTemplatePartKind.Literal);
    }

    [Fact]
    public void EnabledTokens_HaveDisplayBrushKeys()
    {
        foreach (var token in QueryTokenCatalog.Enabled)
        {
            token.DisplayBrushKey.Should().NotBeNullOrWhiteSpace(token.Id);
        }
    }
}

public class QuerySearchEstimatorTests
{
    [Fact]
    public void Estimate_QualityTemplatesMultiplyByQualityCount()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .QueryTemplates(
                "{title} S{season:00}E{episode:00} {quality}",
                "{title} {year} S{season:00}E{episode:00} {quality}",
                "{title} {season}x{episode:00} {quality}")
            .QualityAllowList("2160p", "1080p")
            .Build();

        var estimate = QuerySearchEstimator.Estimate(recipe);

        estimate.AcceptedCount.Should().Be(3);
        estimate.RejectedCount.Should().Be(0);
        estimate.SearchesPerTitle.Should().Be(6);
        estimate.TitleBudget.Should().Be(1);
        estimate.EstimatedMaxSearches.Should().Be(6);
    }

    [Fact]
    public void Estimate_ExcludesRejectedTemplates()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .QueryTemplates("{title} {quality} {audio}", "{title} {year}")
            .QualityAllowList("1080p")
            .Build();

        var estimate = QuerySearchEstimator.Estimate(recipe);

        estimate.AcceptedCount.Should().Be(1);
        estimate.RejectedCount.Should().Be(1);
        estimate.SearchesPerTitle.Should().Be(1);
        estimate.RejectedReasons.Should().Contain(reason => reason.Contains("{audio}"));
    }

    [Fact]
    public void Estimate_TemplatesOverrideUsesWorkingList()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .QueryTemplates("{title} {quality}")
            .QualityAllowList("1080p")
            .Build();

        var estimate = QuerySearchEstimator.Estimate(
            recipe,
            ["{title} {quality}", "{title} {year}"]);

        estimate.AcceptedCount.Should().Be(2);
        estimate.SearchesPerTitle.Should().Be(1 + 1);
    }

    [Fact]
    public void Estimate_TitleBudgetIncludesAliasesAndLibraryCap()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .WithAliases("BCS")
            .UseLibraryEnglishTitles(4)
            .QueryTemplates("{title} {quality}")
            .QualityAllowList("1080p")
            .Build();

        var estimate = QuerySearchEstimator.Estimate(recipe);

        estimate.TitleBudget.Should().Be(1 + 1 + 4);
        estimate.EstimatedMaxSearches.Should().Be(6);
    }

    [Fact]
    public void Estimate_SkipDefaultTitleWithoutAliasesStillHasBudgetOfOne()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .SkipDefaultTitle()
            .QueryTemplates("{title}")
            .Build();

        QuerySearchEstimator.GetTitleBudget(recipe).Should().Be(1);
    }

    [Fact]
    public void FormatHuntEstimate_UsesMaxSearchesAndSkippedTemplates()
    {
        QuerySearchEstimator.FormatHuntEstimate(new QuerySearchEstimate
        {
            EstimatedMaxSearches = 24,
            RejectedCount = 0
        }).Should().Be("Up to 24 searches per hunt");

        QuerySearchEstimator.FormatHuntEstimate(new QuerySearchEstimate
        {
            EstimatedMaxSearches = 1,
            RejectedCount = 1
        }).Should().Be("Up to 1 search per hunt · 1 template skipped");
    }
}

public class QueryHuntEstimateInputsTests
{
    [Fact]
    public void AffectsExtensionKey_MarksTitleBudgetKeysOnly()
    {
        QueryHuntEstimateInputs.AffectsExtensionKey(RecipeRuntimeSettings.SkipDefaultTitleKey).Should().BeTrue();
        QueryHuntEstimateInputs.AffectsExtensionKey(RecipeRuntimeSettings.UseLibraryEnglishTitlesKey).Should().BeTrue();
        QueryHuntEstimateInputs.AffectsExtensionKey(RecipeRuntimeSettings.MaxLibraryAlternativeTitlesForSearchKey).Should().BeTrue();
        QueryHuntEstimateInputs.AffectsExtensionKey(RecipeRuntimeSettings.SanitizeQueryKey).Should().BeFalse();
    }
}

public class QueryTemplatePreviewTests
{
    [Fact]
    public void Preview_SampleTvTemplateRendersPaddedEpisode()
    {
        var result = QueryTemplatePreview.Preview(
            "{title} S{season:00}E{episode:00} {quality}",
            MediaKind.TvEpisode,
            QueryTemplatePreview.CreateSampleContext(MediaKind.TvEpisode));

        result.IsRejected.Should().BeFalse();
        result.Rendered.Should().Be("Better Call Saul S01E01 1080p");
        result.Spans.Should().Contain(span => span.Id == QueryTokenCatalog.QualityId);
    }

    [Fact]
    public void Preview_AudioTokenIsRejected()
    {
        var result = QueryTemplatePreview.Preview(
            "{title} {audio}",
            MediaKind.TvEpisode,
            QueryTemplatePreview.CreateSampleContext(MediaKind.TvEpisode));

        result.IsRejected.Should().BeTrue();
        result.Rendered.Should().BeNull();
        result.RejectedTokenId.Should().Be(QueryTokenCatalog.AudioId);
        result.ExpansionSample.Should().BeEmpty();
    }

    [Fact]
    public void Preview_OverlaysRecipeNameAndFirstQuality()
    {
        var recipe = RecipeBuilder.TvEpisode("High Quality Filter")
            .QueryTemplates("{title} {quality}")
            .QualityAllowList("2160p", "1080p")
            .Build();

        var result = QueryTemplatePreview.Preview("{title} {quality}", recipe);

        result.IsRejected.Should().BeFalse();
        result.Rendered.Should().Be("High Quality Filter 2160p");
        result.ExpansionSample.Should().Equal("High Quality Filter 2160p", "High Quality Filter 1080p");
    }

    [Fact]
    public void PreviewValidExpansions_OmitsRejectedAndUsesShortLongTitles()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .QueryTemplates("{title} {quality}", "{title} {audio}")
            .QualityAllowList("2160p", "1080p")
            .Build();

        var shortLines = QueryTemplatePreview.PreviewValidExpansions(
            recipe,
            QueryPreviewSamples.TvShortTitle,
            recipe.Modules.First(module => module.BlockType == media_management_app.Models.RecipeBlockType.QueryBuilder).QueryTemplates);
        var longLines = QueryTemplatePreview.PreviewValidExpansions(
            recipe,
            QueryPreviewSamples.TvLongTitle,
            recipe.Modules.First(module => module.BlockType == media_management_app.Models.RecipeBlockType.QueryBuilder).QueryTemplates);

        shortLines.Should().Equal(
            "Jujutsu Kaisen 2160p",
            "Jujutsu Kaisen 1080p");
        longLines.Should().Equal(
            "Smoking Behind the Supermarket With You 2160p",
            "Smoking Behind the Supermarket With You 1080p");
        shortLines.Should().NotContain(line => line.Contains("audio", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreviewShortLongByTemplate_GroupsShortThenLongAndOmitsRejected()
    {
        var recipe = RecipeBuilder.TvEpisode()
            .QueryTemplates("{title} {quality}", "{title} {audio}", "{title} {year}")
            .QualityAllowList("2160p", "1080p")
            .Build();

        var templates = recipe.Modules
            .First(module => module.BlockType == media_management_app.Models.RecipeBlockType.QueryBuilder)
            .QueryTemplates;
        var lines = QueryTemplatePreview.PreviewShortLongByTemplate(recipe, templates);

        lines.Select(line => line.Text).Should().Equal(
            "Jujutsu Kaisen 2160p",
            "Smoking Behind the Supermarket With You 2160p",
            "Jujutsu Kaisen 2015",
            "Smoking Behind the Supermarket With You 2015");
        lines.Select(line => line.IsPairStart).Should().Equal(true, false, true, false);
        lines.Should().NotContain(line => line.Text.Contains("audio", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateContext_TitleOverrideDoesNotUseRecipeName()
    {
        var recipe = RecipeBuilder.TvEpisode("Recipe Name").Build();
        var context = QueryTemplatePreview.CreateContext(recipe, QueryPreviewSamples.TvShortTitle);

        context.Title.Should().Be("Jujutsu Kaisen");
    }
}
