using media_management_app.Models;

namespace media_management_app.ViewModels.Filters;

public sealed class SourceItemStateFilter : ISourceItemFilter
{
    private readonly Func<ItemState?> _selectedState;

    public SourceItemStateFilter(Func<ItemState?> selectedState)
    {
        _selectedState = selectedState;
    }

    public bool IsActive => _selectedState() is not null;

    public bool Matches(SourceItem item)
    {
        var state = _selectedState();
        return state is null || item.State == state;
    }
}
