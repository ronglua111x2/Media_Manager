using CommunityToolkit.Mvvm.ComponentModel;

namespace media_management_app.ViewModels;

public sealed partial class AlternativeTitleChipViewModel : ObservableObject
{
    public AlternativeTitleChipViewModel(long mediaId, bool isMovie, string title, bool isExcludedFromRecipeSearch)
    {
        MediaId = mediaId;
        IsMovie = isMovie;
        Title = title;
        _isExcludedFromRecipeSearch = isExcludedFromRecipeSearch;
    }

    public long MediaId { get; }

    public bool IsMovie { get; }

    public string Title { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private bool _isExcludedFromRecipeSearch;

    public string ToolTipText => IsExcludedFromRecipeSearch
        ? "Excluded from recipe search. Middle-click to include again. Left-click to copy."
        : "Included in recipe search. Middle-click to exclude. Left-click to copy.";
}
