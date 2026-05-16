using media_management_app.Models;

namespace media_management_app.ViewModels.Filters;

public interface ISourceItemFilter
{
    bool IsActive { get; }

    bool Matches(SourceItem item);
}
