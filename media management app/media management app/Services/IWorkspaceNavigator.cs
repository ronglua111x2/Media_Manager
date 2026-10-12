using media_management_app.Common;

namespace media_management_app.Services;

public interface IWorkspaceNavigator
{
    void Bind(Action<AppWorkspaceKind> navigate);

    void NavigateTo(AppWorkspaceKind workspace);
}

public sealed class WorkspaceNavigator : IWorkspaceNavigator
{
    private Action<AppWorkspaceKind>? _navigate;

    public void Bind(Action<AppWorkspaceKind> navigate)
    {
        _navigate = navigate;
    }

    public void NavigateTo(AppWorkspaceKind workspace)
    {
        _navigate?.Invoke(workspace);
    }
}
