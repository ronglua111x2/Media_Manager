using media_management_app.Common;
using media_management_app.Models;
using media_management_app.Services;

namespace MediaManager.Core.Tests.Fixtures;

public sealed class RecipeBuilder
{
    private readonly SearchRecipe _recipe;

    private RecipeBuilder(SearchRecipe recipe)
    {
        _recipe = recipe;
    }

    public static RecipeBuilder TvEpisode(string name = "Default TV")
    {
        return new RecipeBuilder(new SearchRecipe
        {
            Name = name,
            TargetKind = MediaKind.TvEpisode,
            Modules =
            [
                Module(RecipeBlockType.Identity),
                Module(RecipeBlockType.QueryBuilder, query =>
                {
                    query.QueryTemplates = ["{title} S{season:00}E{episode:00} {quality}"];
                    query.QualityAllowList = ["1080p"];
                }),
                Module(RecipeBlockType.CandidateFilter, filter =>
                {
                    filter.QualityAllowList = ["1080p"];
                    filter.MinimumSeeders = 0;
                }),
                Module(RecipeBlockType.CandidateParser),
                Module(RecipeBlockType.Scoring)
            ]
        });
    }

    public static RecipeBuilder Movie(string name = "Default Movie")
    {
        return new RecipeBuilder(new SearchRecipe
        {
            Name = name,
            TargetKind = MediaKind.Movie,
            Modules =
            [
                Module(RecipeBlockType.Identity),
                Module(RecipeBlockType.QueryBuilder, query =>
                {
                    query.QueryTemplates = ["{title} {year} {quality}"];
                    query.QualityAllowList = ["1080p"];
                }),
                Module(RecipeBlockType.CandidateFilter, filter =>
                {
                    filter.QualityAllowList = ["1080p"];
                    filter.MinimumSeeders = 0;
                }),
                Module(RecipeBlockType.CandidateParser),
                Module(RecipeBlockType.Scoring)
            ]
        });
    }

    public RecipeBuilder WithAliases(params string[] aliases)
    {
        Identity().Aliases = [.. aliases];
        return this;
    }

    public RecipeBuilder SkipDefaultTitle()
    {
        Query().ExtensionData[RecipeRuntimeSettings.SkipDefaultTitleKey] = bool.TrueString;
        return this;
    }

    public RecipeBuilder UseLibraryEnglishTitles(int max = 4)
    {
        Identity().ExtensionData[RecipeRuntimeSettings.UseLibraryEnglishTitlesKey] = bool.TrueString;
        Identity().ExtensionData[RecipeRuntimeSettings.MaxLibraryAlternativeTitlesForSearchKey] = max.ToString();
        return this;
    }

    public RecipeBuilder SanitizeQuery(bool enabled)
    {
        Query().ExtensionData[RecipeRuntimeSettings.SanitizeQueryKey] = enabled.ToString();
        return this;
    }

    public RecipeBuilder CustomQueries(params string[] queries)
    {
        Query().CustomQueries = [.. queries];
        return this;
    }

    public RecipeBuilder QueryTemplates(params string[] templates)
    {
        Query().QueryTemplates = [.. templates];
        return this;
    }

    public RecipeBuilder AnimeAbsolute()
    {
        Parser().ExtensionData[RecipeRuntimeSettings.EpisodeNumberingModeKey] =
            RecipeRuntimeSettings.AnimeAbsoluteEpisodeNumbering;
        return this;
    }

    public RecipeBuilder MinimumSeeders(int seeders)
    {
        Filter().MinimumSeeders = seeders;
        return this;
    }

    public RecipeBuilder QualityAllowList(params string[] qualities)
    {
        Filter().QualityAllowList = [.. qualities];
        Query().QualityAllowList = [.. qualities];
        return this;
    }

    public RecipeBuilder IncludeTerms(params string[] terms)
    {
        Filter().IncludeTerms = [.. terms];
        return this;
    }

    public RecipeBuilder ExcludeTerms(params string[] terms)
    {
        Filter().ExcludeTerms = [.. terms];
        return this;
    }

    public RecipeBuilder BlockedGroups(params string[] groups)
    {
        Filter().BlockedReleaseGroups = [.. groups];
        return this;
    }

    public RecipeBuilder SizeRange(long? minimumBytes, long? maximumBytes)
    {
        Filter().MinimumSizeBytes = minimumBytes;
        Filter().MaximumSizeBytes = maximumBytes;
        return this;
    }

    public SearchRecipe Build() => _recipe;

    private RecipeModuleConfig Identity() => Get(RecipeBlockType.Identity);

    private RecipeModuleConfig Query() => Get(RecipeBlockType.QueryBuilder);

    private RecipeModuleConfig Filter() => Get(RecipeBlockType.CandidateFilter);

    private RecipeModuleConfig Parser() => Get(RecipeBlockType.CandidateParser);

    private RecipeModuleConfig Get(RecipeBlockType type) =>
        _recipe.Modules.First(module => module.BlockType == type);

    private static RecipeModuleConfig Module(RecipeBlockType type, Action<RecipeModuleConfig>? configure = null)
    {
        var module = new RecipeModuleConfig
        {
            BlockType = type,
            IsEnabled = true,
            DisplayName = type.ToString()
        };
        configure?.Invoke(module);
        return module;
    }
}
