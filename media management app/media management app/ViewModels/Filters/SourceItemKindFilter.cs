using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels.Filters;

public sealed class SourceItemKindFilter : ISourceItemFilter
{
    private readonly Func<MediaKind?> _selectedKind;

    public SourceItemKindFilter(Func<MediaKind?> selectedKind)
    {
        _selectedKind = selectedKind;
    }

    public bool IsActive => _selectedKind() is not null;

    public bool Matches(SourceItem item)
    {
        var kind = _selectedKind();
        return kind is null || item.MediaKind == kind;
    }
}
