using media_management_app.Common;

namespace media_management_app.Services;

/// <summary>
/// Short and long preview titles taken from docs/planning/fixtures/torrent-release-names.json.
/// Not loaded from JSON at runtime.
/// </summary>
public static class QueryPreviewSamples
{
    public const string TvShortTitle = "Jujutsu Kaisen";

    public const string TvLongTitle = "Smoking Behind the Supermarket With You";

    public const string MovieShortTitle = "Your Name";

    public const string MovieLongTitle = "Spider Man Across The Spider Verse";

    public static string ShortTitle(MediaKind targetKind) =>
        targetKind == MediaKind.Movie ? MovieShortTitle : TvShortTitle;

    public static string LongTitle(MediaKind targetKind) =>
        targetKind == MediaKind.Movie ? MovieLongTitle : TvLongTitle;
}
