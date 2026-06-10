namespace media_management_app.Models;

public sealed class LibraryDeleteResult
{
    public int RemovedHardlinkCount { get; set; }

    public int HardlinkErrorCount { get; set; }

    public int DeletedShowCount { get; set; }

    public int DeletedMovieCount { get; set; }

    public int DeletedFetchJobCount { get; set; }

    public List<string> Messages { get; } = [];

    public string Summary =>
        $"Removed {RemovedHardlinkCount} hardlink(s), deleted {DeletedShowCount} show(s) and {DeletedMovieCount} movie(s).";
}
