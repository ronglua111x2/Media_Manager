using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;
using Xunit;

namespace MediaManager.Tests;

public sealed class RecipePipelineTests
{
    [Fact]
    public void BuildEpisodeQueries_UsesAliasesYearEpisodeAndQuality()
    {
        var recipe = CreateTvRecipe();
        recipe.Modules.First(module => module.BlockType == RecipeBlockType.Identity).Aliases.Add("Frieren");
        var show = new TrackedShow
        {
            Title = "Frieren Beyond Journey's End",
            FirstAirYear = 2023
        };
        var episode = new TrackedEpisode
        {
            SeasonNumber = 1,
            EpisodeNumber = 2
        };

        var queries = new SearchPlanBuilder().BuildEpisodeQueries(recipe, show, episode);

        Assert.Contains("Frieren S01E02 1080p", queries);
        Assert.Contains("Frieren Beyond Journey's End 2023 S01E02 1080p", queries);
    }

    [Fact]
    public void EvaluateEpisode_RejectsWrongEpisode()
    {
        var recipe = CreateTvRecipe();
        var result = new TorrentSearchResult
        {
            FileName = "Frieren S01E03 1080p WEB-DL",
            FileUrl = "magnet:?xt=urn:btih:abc",
            Seeders = 20
        };

        var evaluated = new CandidateEvaluationService().EvaluateEpisode(
            recipe,
            new TrackedShow { Title = "Frieren", FirstAirYear = 2023 },
            new TrackedEpisode { SeasonNumber = 1, EpisodeNumber = 2, Title = "It Didn't Have to Be Magic" },
            result);

        Assert.Equal(CandidateRejectReason.EpisodeMismatch, evaluated.RejectReason);
    }

    [Fact]
    public void EvaluateMovie_AcceptsMatchingCandidate()
    {
        var recipe = new SearchRecipe
        {
            TargetKind = MediaKind.Movie,
            Modules =
            [
                new RecipeModuleConfig { BlockType = RecipeBlockType.Identity },
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.CandidateFilter,
                    QualityAllowList = ["1080p"],
                    MinimumSeeders = 1
                }
            ]
        };
        var result = new TorrentSearchResult
        {
            FileName = "Dune Part Two 2024 1080p BluRay",
            FileUrl = "magnet:?xt=urn:btih:def",
            Seeders = 50
        };

        var evaluated = new CandidateEvaluationService().EvaluateMovie(
            recipe,
            new TrackedMovie { Title = "Dune Part Two", ReleaseYear = 2024 },
            result);

        Assert.Equal(CandidateRejectReason.None, evaluated.RejectReason);
        Assert.True(evaluated.TotalScore > 0);
    }

    private static SearchRecipe CreateTvRecipe()
    {
        return new SearchRecipe
        {
            TargetKind = MediaKind.TvEpisode,
            Modules =
            [
                new RecipeModuleConfig { BlockType = RecipeBlockType.Identity },
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.QueryBuilder,
                    QualityAllowList = ["1080p"],
                    QueryTemplates =
                    [
                        "{title} S{season:00}E{episode:00} {quality}",
                        "{title} {year} S{season:00}E{episode:00} {quality}"
                    ]
                },
                new RecipeModuleConfig
                {
                    BlockType = RecipeBlockType.CandidateFilter,
                    QualityAllowList = ["1080p"],
                    MinimumSeeders = 1
                }
            ]
        };
    }
}
