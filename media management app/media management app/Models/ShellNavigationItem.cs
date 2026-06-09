using media_management_app.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace media_management_app.Models;

public partial class ShellNavigationItem : ObservableObject
{
    public AppWorkspaceKind Kind { get; init; }

    public string Label { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string IconKind { get; init; } = "Circle";

    [ObservableProperty]
    private bool isSelected;
}
