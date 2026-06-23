using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class PackLinkProgressStepItemViewModel : ObservableObject
{
    public PackLinkProgressStepItemViewModel(PackLinkProgressStep step, string title)
    {
        Step = step;
        Title = title;
    }

    public PackLinkProgressStep Step { get; }

    public string Title { get; }

    [ObservableProperty]
    private PackLinkProgressStatus status = PackLinkProgressStatus.Pending;

    [ObservableProperty]
    private string message = string.Empty;

    public bool IsActive => Status == PackLinkProgressStatus.Active;

    public bool IsDone => Status == PackLinkProgressStatus.Done;

    public bool IsFailed => Status == PackLinkProgressStatus.Failed;

    partial void OnStatusChanged(PackLinkProgressStatus value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsFailed));
    }
}
