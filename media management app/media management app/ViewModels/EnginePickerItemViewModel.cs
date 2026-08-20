using CommunityToolkit.Mvvm.ComponentModel;

namespace media_management_app.ViewModels;

public enum EnginePickerMode
{
    Search,
    Quality
}

public sealed partial class EnginePickerItemViewModel : ObservableObject
{
    private bool _parentPickEnabled = true;

    public EnginePickerItemViewModel(string name, string fullName, bool isEnabled, bool isSelected, int rankGroup)
    {
        Name = name;
        FullName = fullName;
        IsEnabled = isEnabled;
        _isSelected = isSelected;
        _rankGroup = rankGroup;
    }

    public string Name { get; }

    public string FullName { get; }

    public bool IsEnabled { get; }

    public bool IsCheckboxEnabled => IsEnabled && _parentPickEnabled;

    public void SetParentPickEnabled(bool parentPickEnabled)
    {
        if (_parentPickEnabled == parentPickEnabled)
        {
            return;
        }

        _parentPickEnabled = parentPickEnabled;
        OnPropertyChanged(nameof(IsCheckboxEnabled));
    }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _rankGroup;

    public string DisplayLabel
    {
        get
        {
            var label = string.IsNullOrWhiteSpace(FullName) ? Name : $"{FullName} ({Name})";
            return IsEnabled ? label : $"{label} — disabled in qBittorrent";
        }
    }

    public string DisabledTooltip =>
        IsEnabled
            ? string.Empty
            : "This plugin is disabled in qBittorrent Search. Enable it in qBittorrent before selecting it here.";

    public string RankBadge => $"#{RankGroup}";

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);

    partial void OnRankGroupChanged(int value)
    {
        OnPropertyChanged(nameof(RankBadge));
        RankChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SelectionChanged;

    public event EventHandler? RankChanged;
}
