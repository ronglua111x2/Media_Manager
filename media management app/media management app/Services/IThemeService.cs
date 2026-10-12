using media_management_app.Common;

namespace media_management_app.Services;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    void Apply(AppTheme theme);
}
