using media_management_app.Common;

namespace media_management_app.Models;

public sealed class UiSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Light;
}
