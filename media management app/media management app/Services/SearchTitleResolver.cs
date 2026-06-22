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
            foreach (var alt in request.LibraryAlternativeTitles.Take(EnglishAlternativeTitleFilter.MaxLibraryTitlesPerResolve))
            {
                Add(alt);
            }
        }

        if (titles.Count == 0)
        {
            titles.Add(string.Empty);
        }

        return titles;
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
            UseLibraryEnglishTitles = identityEnabled && RecipeRuntimeSettings.GetUseLibraryEnglishTitles(identityModule),
            IdentityEnabled = identityEnabled
        };
    }
}
