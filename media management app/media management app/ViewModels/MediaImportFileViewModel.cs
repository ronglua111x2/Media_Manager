using CommunityToolkit.Mvvm.ComponentModel;
using media_management_app.Common;
using media_management_app.Models;

namespace media_management_app.ViewModels;

public sealed partial class MediaImportFileViewModel : ObservableObject
{
    public MediaImportFileViewModel(SourceItem sourceItem)
    {
        SourceItem = sourceItem;
    }

    public SourceItem SourceItem { get; }

    public string FileName => SourceItem.FileName;

    public string Folder => SourceItem.ParentFolder;

    public string ParsedLabel => SourceItem.MediaKind == MediaKind.Movie
        ? SourceItem.MovieYear is null ? SourceItem.MovieTitle ?? string.Empty : $"{SourceItem.MovieTitle} ({SourceItem.MovieYear})"
        : SourceItem.SeasonNumber is null || SourceItem.EpisodeNumber is null
            ? SourceItem.ShowTitle ?? string.Empty
            : $"{SourceItem.ShowTitle} S{SourceItem.SeasonNumber:00}E{SourceItem.EpisodeNumber:00}";

    public string Notes => SourceItem.Notes ?? SourceItem.MatchReason ?? string.Empty;

    [ObservableProperty]
    private bool isIncluded = true;
}
