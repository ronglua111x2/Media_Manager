using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;

namespace media_management_app.ViewModels;

public partial class NotificationPreferenceItemViewModel : ObservableObject
{
    public NotificationPreferenceItemViewModel(
        NotificationKind kind,
        string title,
        string description,
        bool isEnabled)
    {
        Kind = kind;
        Title = title;
        Description = description;
        this.isEnabled = isEnabled;
    }

    public NotificationKind Kind { get; }

    public string Title { get; }

    public string Description { get; }

    [ObservableProperty]
    private bool isEnabled;
}
