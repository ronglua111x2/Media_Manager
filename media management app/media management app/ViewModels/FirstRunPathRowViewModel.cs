using CommunityToolkit.Mvvm.ComponentModel;

namespace media_management_app.ViewModels;

public enum FirstRunPathKind
{
    StateFolder,
    QbittorrentExe,
    WarpCli,
    DownloadFolders,
    SourceFolders,
    JellyfinUrl,
    SymlinkRoot
}

public sealed partial class FirstRunPathRowViewModel : ObservableObject
{
    public FirstRunPathRowViewModel(
        FirstRunPathKind kind,
        string label,
        string value,
        bool canBrowse,
        bool isFileBrowse)
    {
        Kind = kind;
        Label = label;
        Value = value;
        CanBrowse = canBrowse;
        IsFileBrowse = isFileBrowse;
    }

    public FirstRunPathKind Kind { get; }

    public string Label { get; }

    public bool CanBrowse { get; }

    public bool IsFileBrowse { get; }

    [ObservableProperty]
    private string value = string.Empty;
}
