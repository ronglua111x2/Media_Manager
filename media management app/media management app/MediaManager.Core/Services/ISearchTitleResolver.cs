using media_management_app.Models;

namespace media_management_app.Services;

public sealed class SearchTitleResolveRequest
{
    public string PrimaryTitle { get; init; } = string.Empty;

    public IReadOnlyList<string> ManualAliases { get; init; } = [];

    public IReadOnlyList<string> LibraryAlternativeTitles { get; init; } = [];

    public bool SkipDefaultTitle { get; init; }

    public bool UseLibraryEnglishTitles { get; init; }

    public int MaxLibraryAlternativeTitlesForSearch { get; init; }

    public bool IdentityEnabled { get; init; }
}

public interface ISearchTitleResolver
{
    IReadOnlyList<string> Resolve(SearchTitleResolveRequest request);

    SearchTitleResolveRequest CreateRequest(
        SearchRecipe recipe,
        string primaryTitle,
        IReadOnlyList<string> libraryAlternativeTitles);

    /// <summary>
    /// Titles a release may legitimately carry when <b>matching</b> results (native-search P1, SRCH-12).
    /// Unlike search titles this always includes the primary title, manual aliases and every library
    /// alternative title (romaji, original title...), whatever the recipe's search settings: extra titles
    /// only make a correct release easier to accept, they never cost a search query.
    /// </summary>
    IReadOnlyList<string> ResolveForMatching(
        SearchRecipe recipe,
        string primaryTitle,
        IReadOnlyList<string> libraryAlternativeTitles) =>
        Resolve(CreateRequest(recipe, primaryTitle, libraryAlternativeTitles));
}
