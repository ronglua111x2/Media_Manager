using media_management_app.Models;

namespace media_management_app.ViewModels.Filters;

public sealed class SourceItemTextFilter : ISourceItemFilter
{
    private readonly Func<string?> _searchText;

    public SourceItemTextFilter(Func<string?> searchText)
    {
        _searchText = searchText;
    }

    public bool IsActive => !string.IsNullOrWhiteSpace(_searchText());

    public bool Matches(SourceItem item)
    {
        var searchText = _searchText();
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var terms = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term => Contains(item, term));
    }

    private static bool Contains(SourceItem item, string term)
    {
        return Contains(item.DisplayTitle, term) ||
               Contains(item.ShowTitle, term) ||
               Contains(item.MovieTitle, term) ||
               Contains(item.MatchedTitle, term) ||
               Contains(item.FileName, term) ||
               Contains(item.FilePath, term) ||
               Contains(item.LinkedPath, term) ||
               Contains(item.Notes, term);
    }

    private static bool Contains(string? value, string term)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
