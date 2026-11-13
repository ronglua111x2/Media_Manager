using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.Services;

public sealed class SearchTitleResolver : ISearchTitleResolver
{
    public IReadOnlyList<string> Resolve(SearchTitleResolveRequest request)
    {
        if (!request.IdentityEnabled)
        {
            return [request.PrimaryTitle];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var titles = new List<string>();

        void Add(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            var normalized = title.Trim();
            if (!seen.Add(normalized))
            {
                return;
            }

            titles.Add(normalized);
        }

        if (!request.SkipDefaultTitle)
        {
            Add(request.PrimaryTitle);
        }

        foreach (var alias in request.ManualAliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
        {
            Add(alias);
        }

        if (request.UseLibraryEnglishTitles)
        {
            foreach (var alt in EnglishAlternativeTitleFilter.SelectLibraryAlternativeTitlesForSearch(
                         request.LibraryAlternativeTitles,
                         request.PrimaryTitle,
                         request.MaxLibraryAlternativeTitlesForSearch))
            {
                Add(alt);
            }
        }

        if (titles.Count == 0)
        {
            titles.Add(string.Empty);
        }

        return EnglishAlternativeTitleFilter.CollapseTitlesForSearch(titles);
    }

    public IReadOnlyList<string> ResolveForMatching(
        SearchRecipe recipe,
        string primaryTitle,
        IReadOnlyList<string> libraryAlternativeTitles)
    {
        var identityModule = recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.Identity);
        var titles = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var title in new[] { primaryTitle }
                     .Concat(identityModule?.Aliases ?? [])
                     .Concat(libraryAlternativeTitles))
        {
            if (!string.IsNullOrWhiteSpace(title) && seen.Add(title.Trim()))
            {
                titles.Add(title.Trim());
            }
        }

        return titles.Count == 0 ? [string.Empty] : titles;
    }

    public SearchTitleResolveRequest CreateRequest(
        SearchRecipe recipe,
        string primaryTitle,
        IReadOnlyList<string> libraryAlternativeTitles)
    {
        var identityModule = recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.Identity);
        var queryModule = recipe.Modules.FirstOrDefault(module => module.BlockType == RecipeBlockType.QueryBuilder);
        var identityEnabled = identityModule?.IsEnabled == true;

        return new SearchTitleResolveRequest
        {
            PrimaryTitle = primaryTitle,
            ManualAliases = identityEnabled ? identityModule!.Aliases : [],
            LibraryAlternativeTitles = libraryAlternativeTitles,
            SkipDefaultTitle = RecipeRuntimeSettings.GetSkipDefaultTitle(queryModule),
            // Movies search alternative titles unless the recipe explicitly turns it off (SRCH-12):
            // old and foreign films are often released only under their original or romaji title.
            UseLibraryEnglishTitles = identityEnabled && RecipeRuntimeSettings.GetUseLibraryEnglishTitles(recipe),
            MaxLibraryAlternativeTitlesForSearch = identityEnabled
                ? RecipeRuntimeSettings.GetMaxLibraryAlternativeTitlesForSearch(identityModule)
                : 0,
            IdentityEnabled = identityEnabled
        };
    }
}
