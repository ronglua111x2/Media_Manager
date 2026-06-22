using media_management_app.Models;

namespace media_management_app.Services;

public sealed class SearchTitleResolveRequest
{
    public string PrimaryTitle { get; init; } = string.Empty;

    public IReadOnlyList<string> ManualAliases { get; init; } = [];

    public IReadOnlyList<string> LibraryAlternativeTitles { get; init; } = [];

    public bool SkipDefaultTitle { get; init; }

    public bool UseLibraryEnglishTitles { get; init; }

    public bool IdentityEnabled { get; init; }
}

public interface ISearchTitleResolver
{
    IReadOnlyList<string> Resolve(SearchTitleResolveRequest request);

    SearchTitleResolveRequest CreateRequest(
        SearchRecipe recipe,
        string primaryTitle,
        IReadOnlyList<string> libraryAlternativeTitles);
}
