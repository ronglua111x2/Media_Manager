using CommunityToolkit.Mvvm.ComponentModel;

namespace media_management_app.ViewModels;

public partial class QualityOptionViewModel : ObservableObject
{
    private readonly Action _changed;

    public QualityOptionViewModel(string label, bool isSelected, Action changed)
    {
        Label = label;
        _changed = changed;
        this.isSelected = isSelected;
    }

    public string Label { get; }

    [ObservableProperty]
    private bool isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        _changed();
    }
}
